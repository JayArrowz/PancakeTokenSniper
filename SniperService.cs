using BscTokenSniper.Handlers;
using BscTokenSniper.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private bool _disposed;
        private StreamingWebSocketClient _client;
        private readonly List<IDisposable> _disposables = new();
        private readonly RugHandler _rugChecker;
        private readonly TradeHandler _tradeHandler;
        private readonly CancellationTokenSource _processingCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        public SniperService(IOptions<SniperConfiguration> options, RugHandler rugChecker, TradeHandler tradeHandler)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey));
            _factoryContract = _bscWeb3.Eth.GetContract(File.ReadAllText("./Abis/PairCreated.json"), _sniperConfig.PancakeswapFactoryAddress);
            _rugChecker = rugChecker;
            _tradeHandler = tradeHandler;
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

        private async Task<EthLogsObservableSubscription> StartClient()
        {
            _client = new StreamingWebSocketClient(_sniperConfig.BscNode);
            var filter = _bscWeb3.Eth.GetEvent<PairCreatedEvent>(_sniperConfig.PancakeswapFactoryAddress).CreateFilterInput();
            var filterTransfers = Event<PairCreatedEvent>.GetEventABI().CreateFilterInput();
            filterTransfers.Address = new string[1] { _sniperConfig.PancakeswapFactoryAddress };
            var subscription = new EthLogsObservableSubscription(_client);

            _disposables.Add(subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(t =>
            {
                try
                {
                    var decodedEvent = t.DecodeEvent<PairCreatedEvent>();
                    _ = PairCreated(decodedEvent).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Serilog.Log.Logger.Error(e, nameof(StartClient));
                }
            }));

            await _client.StartAsync();
            await subscription.SubscribeAsync(filter);
            return subscription;
        }

        private void KeepAliveClient()
        {
            while (!_disposed)
            {
                var handler = new EthBlockNumberObservableHandler(_client);
                var disposable = handler.GetResponseAsObservable().Subscribe(x => Log.Logger.Information("Current block: {0}", x.Value));
                try
                {
                    handler.SendRequestAsync().Wait(TimeSpan.FromMinutes(5));
                }
                catch (Exception e)
                {
                    Serilog.Log.Logger.Error(e, nameof(KeepAliveClient));
                    Serilog.Log.Logger.Information("Error from websocket, restarting client.");
                    _ = StartClient().Result;
                }
                Thread.Sleep(30000);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await StartClient();
            new Thread(KeepAliveClient).Start();
        }

        private async Task PairCreated(EventLog<PairCreatedEvent> pairCreated)
        {
            try
            {
                var pair = pairCreated.Event;
                var otherPairAddress = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? pair.Token1 : pair.Token0;
                var symbol = await _rugChecker.GetSymbol(pair);
                pair.Symbol = symbol;

                var addressWhitelisted = _sniperConfig.WhitelistedTokens.Any(t => t.Equals(otherPairAddress));
                var rugCheckPassed = _sniperConfig.RugCheckEnabled && !addressWhitelisted ? await _rugChecker.CheckRugAsync(pair) : true;
                var otherTokenIdx = pair.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? 1 : 0;
                var honeypotCheck = !addressWhitelisted && _sniperConfig.HoneypotCheck;

                Log.Logger.Information("Discovered Token Pair {0} Rug check Result: {1} Contract address: {2}", symbol, rugCheckPassed, otherPairAddress);
                if (!rugCheckPassed)
                {
                    Log.Logger.Warning("Rug Check failed for {0}", symbol);
                    return;
                }

                if (!honeypotCheck)
                {
                    if (!addressWhitelisted)
                    {
                        Log.Logger.Information("Buying Token pair: {0}", symbol);
                    }
                    else
                    {
                        Log.Logger.Information("Buying Token pair: {0} WHITELISTED ADDRESS: {1}", symbol, addressWhitelisted);
                    }
                    await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.AmountToSnipe);
                    return;
                }
                Log.Logger.Information("Starting Honeypot check for {0} with amount {1}", symbol, _sniperConfig.HoneypotCheckAmount);
                var buySuccess = await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.HoneypotCheckAmount, true);
                if (!buySuccess)
                {
                    Log.Logger.Fatal("Honeypot check failed could not buy token: {0}", pair.Symbol);
                    return;
                }
                var ownedToken = _tradeHandler.GetOwnedTokens(otherPairAddress);
                await _tradeHandler.Approve(otherPairAddress);
                var marketPrice = await _tradeHandler.GetMarketPrice(ownedToken, ownedToken.Amount - 1);
                var sellSuccess = false;

                try
                {
                    sellSuccess = await _tradeHandler.Sell(otherPairAddress, ownedToken.Amount - 1, marketPrice, _sniperConfig.SellSlippage);
                }
                catch (Exception e)
                {
                    Serilog.Log.Error("Error Sell", e);
                }
                if (!sellSuccess)
                {
                    Log.Logger.Fatal("Honeypot check DETECTED HONEYPOT could not sell token: {0}", pair.Symbol);
                    return;
                }

                Log.Logger.Information("Honeypot check PASSED buying token: {0}", pair.Symbol);
                await _tradeHandler.Buy(otherPairAddress, otherTokenIdx, pair.Pair, _sniperConfig.AmountToSnipe);
            }
            catch (Exception e)
            {
                Log.Logger.Error(nameof(PairCreated), e);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Log.Logger.Information("Stopping SniperService");
            _disposed = true;
            _processingCancellation.Dispose();
            _disposables.ForEach(t => t.Dispose());
            return Task.CompletedTask;
        }
    }
}