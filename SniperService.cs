using BscTokenSniper.Handlers;
using BscTokenSniper.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nethereum.BlockchainProcessing;
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
        private readonly RugHandler _rugChecker;
        private readonly TradeHandler _tradeHandler;
        private readonly CancellationTokenSource _processingCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        private Queue<BigInteger> _blockIdQueue;

        public SniperService(IOptions<SniperConfiguration> options, RugHandler rugChecker, TradeHandler tradeHandler)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey));
            _factoryContract = _bscWeb3.Eth.GetContract(File.ReadAllText("./Abis/PairCreated.json"), _sniperConfig.PancakeswapFactoryAddress);
            _rugChecker = rugChecker;
            _tradeHandler = tradeHandler;
        }

        private async Task ReadLogs(BlockchainProcessor processor, IWeb3 web3, Contract contract, Action<EventLog<PairCreatedEvent>> tokenSwapEvent, Func<HexBigInteger> currentProcessedBlockNumber, HexBigInteger newBlockNumber, Action<HexBigInteger> onBlockUpdate)
        {
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

            Log.Logger.Information("Reading Logs from Block: {fromBlockNumber} to {newBlockNumber}", fromBlockNumber, newBlockNumber);
            onBlockUpdate.Invoke(newBlockNumber);
            try
            {
                await processor.ExecuteAsync(startAtBlockNumberIfNotProcessed: fromBlockNumber,
                    cancellationToken: _processingCancellation.Token,
                    toBlockNumber: newBlockNumber);
            }
            catch (Exception e)
            {
                Serilog.Log.Logger.Error("Error processing block", e);
            }

            Log.Logger.Debug("Processed: {fromBlockNumber} to {newBlockNumber}, Contract address: {Address}", fromBlockNumber, newBlockNumber, contract.Address);

        }

        private void CreateTokenPair(FilterLog log, Action<EventLog<PairCreatedEvent>> onNext)
        {
            var pairCreated = log.DecodeEvent<PairCreatedEvent>();
            Log.Logger.Information("Pair Created Event Found: {@log} Data: {@pairCreated}", log, pairCreated);
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

            var processor = web3.Processing.Logs.CreateProcessorForContract<PairCreatedEvent>(contract.Address,
                eventLog => CreateTokenPair(eventLog.Log, onTokenSwapNext));

            _disposables.Add(newBlockSub.GetSubscriptionDataResponsesAsObservable()
                .Subscribe(newBlockEvent => _ = ReadLogs(processor, web3, contract, onTokenSwapNext, currentProcessedBlockNumber, newBlockEvent.Number, onBlockUpdate)));
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
            try
            {
                var pair = pairCreated.Event;
                var symbol = await _rugChecker.GetSymbol(pair);
                pair.Symbol = symbol;
                var rugCheckPassed = _sniperConfig.RugCheckEnabled ? await _rugChecker.CheckRugAsync(pair) : true;
                var otherPairAddress = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? pair.Token1 : pair.Token0;
                var otherTokenIdx = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? 1 : 0;
                var honeypotCheck = _sniperConfig.HoneypotCheck;

                Log.Logger.Information("Discovered Token Pair {0} Rug check Result: {1}", symbol, rugCheckPassed);
                if (rugCheckPassed)
                {
                    Log.Logger.Information("Buying Token pair: {0}", symbol);

                    if (!honeypotCheck)
                    {
                        await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.AmountToSnipe);
                    }
                    else
                    {
                        Log.Logger.Information("Starting Honeypot check for {0} with amount {1}", symbol, _sniperConfig.HoneypotCheckAmount);
                    }
                }

                if (honeypotCheck)
                {
                    var buySuccess = await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.HoneypotCheckAmount);
                    if (!buySuccess)
                    {
                        Log.Logger.Fatal("Honeypot check failed could not buy token: {0}", pair.Symbol);
                        return;
                    }
                    var ownedToken = _tradeHandler.GetOwnedTokens(otherPairAddress);
                    var marketPrice = await _tradeHandler.GetMarketPrice(ownedToken);
                    var sellSuccess = await _tradeHandler.Sell(otherPairAddress, otherTokenIdx, ownedToken.Amount, marketPrice);
                    if (!sellSuccess)
                    {
                        Log.Logger.Fatal("Honeypot check DETECTED HONEYPOT could not sell token: {0}", pair.Symbol);
                        return;
                    }

                    Log.Logger.Fatal("Honeypot check PASSED buying token: {0}", pair.Symbol);
                    await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.AmountToSnipe);
                }
            } catch(Exception e)
            {
                Log.Logger.Error(nameof(PairCreated), e);
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