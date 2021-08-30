using BscTokenSniper.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BscTokenSniper
{
    public class SniperService : IHostedService
    {
        private readonly Web3 _bscWeb3;
        private readonly Contract _factoryContract;
        private SniperConfiguration _sniperConfig;
        private HexBigInteger _bscBlockNumber;
        private readonly List<IDisposable> _disposables = new();
        private readonly RugChecker _rugChecker;
        private readonly TradeHandler _tradeHandler;
        private readonly CancellationTokenSource _processingCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        public SniperService(IOptions<SniperConfiguration> options, RugChecker rugChecker, TradeHandler tradeHandler)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey));
            _factoryContract = _bscWeb3.Eth.GetContract(File.ReadAllText("./Abis/PairCreated.json"), _sniperConfig.PancakeswapFactoryAddress);
            _rugChecker = rugChecker;
            _tradeHandler = tradeHandler;
        }

        private async Task ReadLogs(IWeb3 web3, Contract contract, Action<EventLog<PairCreatedEvent>> tokenSwapEvent, Func<HexBigInteger> currentProcessedBlockNumber, HexBigInteger newBlockNumber, Action<HexBigInteger> onBlockUpdate)
        {
            var processor = web3.Processing.Logs.CreateProcessorForContract<PairCreatedEvent>(contract.Address,
                eventLog => CreateTokenPair(eventLog.Log, tokenSwapEvent));
            var currentProcessedBlock = currentProcessedBlockNumber.Invoke();
            var isBehind = currentProcessedBlock != null && BigInteger.Subtract(newBlockNumber.Value, currentProcessedBlock.Value) > 1;
            if (isBehind)
            {
                Log.Logger.Warning("Log processing has missed more than one block: From: {currentProcessedBlock}, To: {newBlockNumber}", currentProcessedBlock, newBlockNumber);
            }

            HexBigInteger fromBlockNumber =
                currentProcessedBlock == null ?
                    new HexBigInteger(BigInteger.Subtract(newBlockNumber.Value, new BigInteger(1)))
                    : (isBehind ? currentProcessedBlock : newBlockNumber);

            Log.Logger.Information("Reading Logs from Block: {fromBlockNumber} to {newBlockNumber}, Contract address: {Address}", fromBlockNumber, newBlockNumber, contract.Address);
            onBlockUpdate.Invoke(newBlockNumber);
            try
            {
                await processor.ExecuteAsync(startAtBlockNumberIfNotProcessed: fromBlockNumber,
                    cancellationToken: _processingCancellation.Token,
                    toBlockNumber: newBlockNumber);
            } catch(Exception e)
            {
                Serilog.Log.Logger.Error("Error processing block", e);
            }

            Log.Logger.Information("Processed: {fromBlockNumber} to {newBlockNumber}, Contract address: {Address}", fromBlockNumber, newBlockNumber, contract.Address);

        }

        private void CreateTokenPair(FilterLog log, Action<EventLog<PairCreatedEvent>> onNext)
        {
            var pairCreated = log.DecodeEvent<PairCreatedEvent>();
            Log.Logger.Information("Pair Created Event: {@log} Data: {@pairCreated}", log, pairCreated);
            if (onNext != null)
            {
                onNext.Invoke(pairCreated);
            }
        }

        private async Task SubscribeEvent(Contract contract, Func<HexBigInteger> currentProcessedBlockNumber, string wssPath, IWeb3 web3, Action<EventLog<PairCreatedEvent>> onTokenSwapNext, Action<HexBigInteger> onBlockUpdate)
        {
            var client = new StreamingWebSocketClient(wssPath);
            _disposables.Add(client);
            var newBlockSub = new EthNewBlockHeadersObservableSubscription(client);
            _disposables.Add(newBlockSub.GetSubscriptionDataResponsesAsObservable()
                .Subscribe(newBlockEvent => _ = ReadLogs(web3, contract, onTokenSwapNext, currentProcessedBlockNumber, newBlockEvent.Number, onBlockUpdate)));
            await client.StartAsync();
            await newBlockSub.SubscribeAsync();
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var bscBlockNumber = await _bscWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var bscWalletPublicKey = EthECKey.GetPublicAddress(_sniperConfig.WalletPrivateKey);
            void UpdateBscBlock(HexBigInteger newBscBlock)
            {
                _bscBlockNumber = newBscBlock;
            }

            await SubscribeEvent(_factoryContract, () => _bscBlockNumber, _sniperConfig.BscNode, _bscWeb3, (e) => _ = PairCreated(e).ConfigureAwait(false), UpdateBscBlock);
        }

        private async Task PairCreated(EventLog<PairCreatedEvent> pairCreated)
        {
            var pair = pairCreated.Event;
            var rugCheckPassed = _sniperConfig.RugCheckEnabled ? await _rugChecker.CheckRugAsync(pair) : true;
            var otherPairAddress = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? pair.Token1 : pair.Token0;
            var otherTokenIdx = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? 1 : 0;

            Log.Logger.Information("Rug Checked Pair: {0} - {1} Result: {2}", pair.Token0, pair.Token1, rugCheckPassed);

            if (rugCheckPassed)
            {
                Log.Logger.Information("Buying Token pair: {0} : {1}", pair.Token0, pair.Token1);
                await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Log.Logger.Information("Stopping SniperService");
            _processingCancellation.Dispose();
            _disposables.ForEach(t => t.Dispose());
            return Task.CompletedTask;
        }
    }
}