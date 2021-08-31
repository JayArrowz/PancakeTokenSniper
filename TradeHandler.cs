using BscTokenSniper.Models;
using Fractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
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
    public class TradeHandler : IDisposable
    {
        private readonly SniperConfiguration _sniperConfig;
        private readonly Web3 _bscWeb3;
        private readonly Contract _pancakeContract;
        private readonly RugChecker _rugChecker;
        private List<TokensOwned> _ownedTokenList = new();
        private bool _stopped;
        private readonly string _erc20Abi;

        public TradeHandler(IOptions<SniperConfiguration> options, RugChecker rugChecker)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey, new BigInteger(_sniperConfig.ChainId)));
            _bscWeb3.TransactionManager.UseLegacyAsDefault = true;
            _erc20Abi = File.ReadAllText("./Abis/Erc20.json");
            _pancakeContract = _bscWeb3.Eth.GetContract(File.ReadAllText("./Abis/Pancake.json"), _sniperConfig.PancakeswapRouterAddress);
            _rugChecker = rugChecker;
            Start();
        }

        public async Task<bool> Buy(string tokenAddress, int tokenIdx, string pairAddress, double amt)
        {
            try
            {
                var buyFunction = _pancakeContract.GetFunction("swapExactETHForTokens");
                var gas = new HexBigInteger(_sniperConfig.GasAmount);
                var amount = new HexBigInteger(Web3.Convert.ToWei(amt));
                var buyReturnValue = await buyFunction.SendTransactionAsync(_sniperConfig.WalletAddress, gas, amount, 0,
                    new string[] { _sniperConfig.LiquidityPairAddress, tokenAddress },
                    _sniperConfig.WalletAddress,
                    (DateTime.UtcNow.Ticks + _sniperConfig.TransactionRevertTimeSeconds));
                var reciept = await _bscWeb3.TransactionManager.TransactionReceiptService.PollForReceiptAsync(buyReturnValue, new CancellationTokenSource(TimeSpan.FromMinutes(2)));
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
                        SinglePrice = new Fraction(balance).Divide(new Fraction(amount.Value)).ToDouble(),
                        TokenIdx = tokenIdx,
                        PairAddress = pairAddress,
                        Decimals = decimals
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

        public async Task<bool> Sell(string tokenAddress, int tokenIdx, BigInteger amount, BigInteger outAmount)
        {
            try
            {
                var sellFunction = _pancakeContract.GetFunction<SwapExactTokensForETHSupportingFeeOnTransferTokensFunction>();

                var gas = new HexBigInteger(_sniperConfig.GasAmount);
                var transactionAmount = new BigInteger((decimal)amount).ToHexBigInteger();
                var txId = await sellFunction.SendTransactionAsync(new SwapExactTokensForETHSupportingFeeOnTransferTokensFunction
                {
                    AmountOutMin = outAmount,
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

        public async Task<BigInteger> GetMarketPrice(TokensOwned ownedToken)
        {
            var price = await _rugChecker.GetReserves(ownedToken.PairAddress);
            var pricePerLiquidityToken = ownedToken.TokenIdx == 1 ? new Fraction(price.Reserve1).Divide(price.Reserve0).ToDouble() : new Fraction(price.Reserve0).Divide(price.Reserve1).ToDouble();
            return new Fraction(pricePerLiquidityToken).Multiply(ownedToken.Amount).ToBigInteger();
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
                    var price = _rugChecker.GetReserves(ownedToken.PairAddress).Result;
                    var pricePerLiquidityToken = ownedToken.TokenIdx == 1 ? new Fraction(price.Reserve1).Divide(price.Reserve0).ToDouble() : new Fraction(price.Reserve0).Divide(price.Reserve1).ToDouble();
                    var profitPerc = 100.0 - ((100.0 / ownedToken.SinglePrice) * pricePerLiquidityToken);
                    Log.Logger.Information("Token: {0} Price bought: {1} Current Price: {2} Current Profit: {3}%",
                        ownedToken.Address, ownedToken.SinglePrice, pricePerLiquidityToken, profitPerc);

                    if (profitPerc > _sniperConfig.ProfitPercentageMargin)
                    {
                        Sell(ownedToken.Address, ownedToken.TokenIdx, ownedToken.Amount, new Fraction(pricePerLiquidityToken).Multiply(ownedToken.Amount).ToBigInteger()).Wait();
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
