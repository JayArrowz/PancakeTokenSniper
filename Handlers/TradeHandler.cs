using BscTokenSniper.Models;
using Fractions;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
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

namespace BscTokenSniper.Handlers
{
    public class TradeHandler : IDisposable
    {
        private readonly SniperConfiguration _sniperConfig;
        private readonly Web3 _bscWeb3;
        private readonly Contract _pancakeContract;
        private readonly RugHandler _rugChecker;
        private List<TokensOwned> _ownedTokenList = new();
        private bool _stopped;
        private readonly string _erc20Abi;
        private readonly string _pairAbi;
        private static BigInteger Max { get; } = BigInteger.Parse("115792089237316195423570985008687907853269984665640564039457584007913129639935");

        public TradeHandler(IOptions<SniperConfiguration> options, RugHandler rugChecker)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey, new BigInteger(_sniperConfig.ChainId)));
            _bscWeb3.TransactionManager.UseLegacyAsDefault = true;
            _erc20Abi = File.ReadAllText("./Abis/Erc20.json");
            _pairAbi = File.ReadAllText("./Abis/Pair.json");
            _pancakeContract = _bscWeb3.Eth.GetContract(File.ReadAllText("./Abis/Pancake.json"), _sniperConfig.PancakeswapRouterAddress);
            _rugChecker = rugChecker;
            Start();
        }

        public async Task<bool> Buy(string tokenAddress, int tokenIdx, string pairAddress, double amt, bool honeypotCheck = false)
        {
            try
            {
                if (_ownedTokenList.Any(t => t.Address == tokenAddress)) {

                    Log.Logger.Information("[CANNOT BUY] Token: {0} Cause: {1}", tokenAddress, "Already has token");
                    return false;
                }
                if (_sniperConfig.BuyDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_sniperConfig.BuyDelaySeconds));
                }
                var buyFunction = _pancakeContract.GetFunction("swapExactETHForTokens");
                var gas = new HexBigInteger(_sniperConfig.GasAmount);
                var amount = new HexBigInteger(Web3.Convert.ToWei(amt));
                var buyReturnValue = await buyFunction.SendTransactionAsync(_sniperConfig.WalletAddress, gas, amount, 0,
                    new string[] { _sniperConfig.LiquidityPairAddress, tokenAddress },
                    _sniperConfig.WalletAddress,
                    (DateTime.UtcNow.Ticks + _sniperConfig.TransactionRevertTimeSeconds));
                var reciept = await _bscWeb3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(buyReturnValue, new CancellationTokenSource(TimeSpan.FromMinutes(2)));
                var sellPrice = await GetMarketPrice(new TokensOwned
                {
                    PairAddress = pairAddress,
                    TokenIdx = tokenIdx
                }, amount);
                Log.Logger.Information("[BUY] TX ID: {buyReturnValue} Reciept: {@reciept}", buyReturnValue, reciept);

                var swapEventList = reciept.DecodeAllEvents<SwapEvent>().Where(t => t.Event != null)
                    .Select(t => t.Event).ToList();
                var swapEvent = swapEventList.FirstOrDefault();
                if (swapEvent != null)
                {
                    var balance = tokenIdx == 0 ? swapEvent.Amount0Out : swapEvent.Amount1Out;
                    var erc20Contract = _bscWeb3.Eth.GetContract(_erc20Abi, tokenAddress);
                    var decimals = await erc20Contract.GetFunction("decimals").CallAsync<int>();
                    _ownedTokenList.Add(new TokensOwned
                    {
                        Address = tokenAddress,
                        Amount = balance,
                        BnbAmount = amount,
                        SinglePrice = sellPrice,
                        TokenIdx = tokenIdx,
                        PairAddress = pairAddress,
                        Decimals = decimals,
                        HoneypotCheck = honeypotCheck
                    });
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Log.Logger.Error("Error buying", e);
                return false;
            }
        }

        public TokensOwned GetOwnedTokens(string tokenAddress)
        {
            return _ownedTokenList.FirstOrDefault(t => t.Address == tokenAddress);
        }

        public async Task<bool> Approve(string tokenAddress)
        {
            try
            {
                var gas = new HexBigInteger(_sniperConfig.GasAmount);
                var pairContract = _bscWeb3.Eth.GetContract(_pairAbi, tokenAddress);
                var approveFunction = pairContract.GetFunction<ApproveFunction>();
                var approve = await approveFunction.SendTransactionAndWaitForReceiptAsync(new ApproveFunction
                {
                    Spender = _sniperConfig.PancakeswapRouterAddress,
                    Value = Max
                }, _sniperConfig.WalletAddress, gas, new HexBigInteger(BigInteger.Zero));
            }
            catch (Exception e)
            {
                Serilog.Log.Logger.Warning("Could not approve sell for {0}", tokenAddress);
            }
            return true;
        }

        public async Task<bool> Sell(string tokenAddress, BigInteger amount, BigInteger outAmount, double slippage)
        {
            try
            {
                var sellFunction = _pancakeContract.GetFunction<SwapExactTokensForETHSupportingFeeOnTransferTokensFunction>();

                var gas = new HexBigInteger(_sniperConfig.GasAmount);
                var transactionAmount = new BigInteger((decimal)amount).ToHexBigInteger();

                var txId = await sellFunction.SendTransactionAsync(new SwapExactTokensForETHSupportingFeeOnTransferTokensFunction
                {
                    AmountOutMin = slippage == 0 ? outAmount : (new Fraction(outAmount).Subtract(new Fraction(slippage/100.0).Multiply(outAmount)).ToBigInteger()),
                    AmountIn = amount,
                    Path = new List<string>() { tokenAddress, _sniperConfig.LiquidityPairAddress },
                    To = _sniperConfig.WalletAddress,
                    Deadline = new BigInteger(DateTime.UtcNow.Ticks + _sniperConfig.TransactionRevertTimeSeconds)
                }, _sniperConfig.WalletAddress, gas, new HexBigInteger(BigInteger.Zero));
                var reciept = await _bscWeb3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txId, new CancellationTokenSource(TimeSpan.FromMinutes(2)));
                Log.Logger.Information("[SELL] TX ID: {txId} Reciept: {@reciept}", txId, reciept);

                var swapEventList = reciept.DecodeAllEvents<SwapEvent>().Where(t => t.Event != null)
                    .Select(t => t.Event).ToList();
                var swapEvent = swapEventList.FirstOrDefault();
                if (swapEvent != null)
                {
                    var item = _ownedTokenList.FirstOrDefault(t => t.Address == tokenAddress);
                    _ownedTokenList.Remove(item);
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Log.Logger.Error("Error selling", e);
                return false;
            }
        }

        private BigInteger GetMarketPrice(Reserves price, TokensOwned ownedToken, BigInteger amount)
        {
            if (price.Reserve0 == 0 || price.Reserve1 == 0)
            {
                return BigInteger.Zero;
            }
            var pricePerLiquidityToken = ownedToken.TokenIdx == 1 ? new Fraction(price.Reserve0).Divide(price.Reserve1) : new Fraction(price.Reserve1).Divide(price.Reserve0);

            return ((BigInteger)pricePerLiquidityToken.Multiply(amount));
        }

        public async Task<BigInteger> GetMarketPrice(TokensOwned ownedToken, BigInteger amount)
        {
            var price = await _rugChecker.GetReserves(ownedToken.PairAddress);
            return GetMarketPrice(price, ownedToken, amount);
        }

        public void Start()
        {
            new Thread(new ThreadStart(MonitorPrices)).Start();
        }

        private void MonitorPrices()
        {
            while (!_stopped)
            {
                for (int i = _ownedTokenList.Count - 1; i >= 0; i--)
                {
                    var ownedToken = _ownedTokenList[i];
                    if(ownedToken.FailedSell || ownedToken.HoneypotCheck)
                    {
                        continue;
                    }
                    var price = _rugChecker.GetReserves(ownedToken.PairAddress).Result;
                    if(price.Reserve0 == 0 || price.Reserve1 == 0)
                    {
                        continue;
                    }

                    var currentPrice = GetMarketPrice(price, ownedToken, ownedToken.BnbAmount);
                    var profitPerc = new Fraction(currentPrice).Subtract(ownedToken.SinglePrice).Divide(ownedToken.SinglePrice).Multiply(100);
                    Log.Logger.Information("Token: {0} Price bought: {1} Current Price: {2} Current Profit: {3}%",
                        ownedToken.Address, ((decimal)ownedToken.SinglePrice), ((decimal)currentPrice), ((decimal)profitPerc));

                    if (profitPerc > new Fraction(_sniperConfig.ProfitPercentageMargin))
                    {
                        try
                        {
                            ownedToken.FailedSell = !Sell(ownedToken.Address, ownedToken.Amount - 1, GetMarketPrice(ownedToken, ownedToken.Amount - 1).Result, _sniperConfig.SellSlippage).Result;
                        } catch(Exception e)
                        {
                            Serilog.Log.Logger.Error(nameof(MonitorPrices), e);
                            ownedToken.FailedSell = true;
                        }
                        _ownedTokenList.Remove(ownedToken);
                    }
                }
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        public void Dispose()
        {
            _stopped = true;
        }
    }
}
