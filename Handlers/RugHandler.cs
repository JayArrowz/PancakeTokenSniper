using BscTokenSniper.Models;
using Fractions;
using Microsoft.Extensions.Options;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

namespace BscTokenSniper.Handlers
{
    public class RugHandler
    {
        private readonly string GetSourceUrl = "https://api.bscscan.com/api?module=contract&action=getsourcecode&address={0}&apikey={1}";
        private readonly string RugdocCheckUrl = "https://honeypot.api.rugdoc.io/api/honeypotStatus.js?address={0}&chain=bsc";
        private HttpClient _httpClient;
        private SniperConfiguration _sniperConfig;
        private readonly string _erc20Abi;
        private readonly Web3 _bscWeb3;
        private readonly string _pairContractStr;
        public RugHandler(IOptions<SniperConfiguration> sniperConfig, IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _sniperConfig = sniperConfig.Value;
            _erc20Abi = File.ReadAllText("./Abis/Erc20.json");
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey));
            _pairContractStr = File.ReadAllText("./Abis/Pair.json");
        }

        public async Task<bool> RugdocCheck(string token)
        {
            if(!_sniperConfig.RugdocCheckEnabled)
            {
                return true;
            }
            try
            {
                var response = await _httpClient.GetAsync(string.Format(RugdocCheckUrl, token));
                var rugdocStr = await response.Content.ReadAsStringAsync();
                var responseObject = JObject.Parse(rugdocStr);
                var valid = responseObject["status"].Value<string>().Equals("OK", StringComparison.InvariantCultureIgnoreCase);
                Serilog.Log.Logger.Information("Rugdoc check token {0} Status: {1} RugDoc Response: {2}", token, valid, rugdocStr);
                return valid;
            }
            catch (Exception e)
            {
                Serilog.Log.Error(nameof(RugdocCheck), e);
                return false;
            }
        }

        public async Task<string> GetSymbol(PairCreatedEvent pairCreatedEvent)
        {
            var otherPairAddress = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ?
                pairCreatedEvent.Token1 : pairCreatedEvent.Token0;
            return await _bscWeb3.Eth.GetContract(_erc20Abi, otherPairAddress).GetFunction("symbol").CallAsync<string>();
        }

        public async Task<bool> CheckRugAsync(PairCreatedEvent pairCreatedEvent)
        {
            if (pairCreatedEvent.Token0 != _sniperConfig.LiquidityPairAddress && pairCreatedEvent.Token1 != _sniperConfig.LiquidityPairAddress)
            {
                Serilog.Log.Logger.Warning("Target liquidity pair found for pair: {0} - {1}. Not bought", pairCreatedEvent.Token0, pairCreatedEvent.Token0);
                return false;
            }

            var otherPairAddress = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ?
                pairCreatedEvent.Token1 : pairCreatedEvent.Token0;
            var otherPairIdx = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ?
                1 : 0;
            Task<bool>[] rugCheckerTasks = new Task<bool>[] {
                RugdocCheck(otherPairAddress),
                CheckContractVerified(otherPairAddress),
                CheckMinLiquidity(pairCreatedEvent, otherPairAddress, otherPairIdx)
            };
            await Task.WhenAll(rugCheckerTasks);
            return rugCheckerTasks.All(t => t.IsCompletedSuccessfully && t.Result);
        }

        private async Task<bool> CheckMinLiquidity(PairCreatedEvent pairCreatedEvent, string token, int otherPairIdx)
        {
            var currentPair = pairCreatedEvent.Pair;
            var reserves = await GetReserves(currentPair);
            var totalAmount = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? reserves.Reserve0 : reserves.Reserve1;

            var result = totalAmount >= Web3.Convert.ToWei(_sniperConfig.MinLiquidityAmount);
            var amountStr = Web3.Convert.FromWei(totalAmount).ToString();
            if (!result)
            {
                Serilog.Log.Logger.Warning("Not enough liquidity added to token {0}. Not buying. Only {1} liquidity added", token, amountStr);
                return result;
            }
            else
            {
                Serilog.Log.Logger.Information("Min Liqudity check for {0} token passed. Liqudity amount: {1}", currentPair, amountStr);
            }

            if (_sniperConfig.MinimumPercentageOfTokenInLiquidityPool > 0)
            {
                var ercContract = _bscWeb3.Eth.GetContract(_erc20Abi, token);
                var balanceOfFunction = ercContract.GetFunction("balanceOf");
                var tokenAmountInPool = otherPairIdx == 1 ? reserves.Reserve1 : reserves.Reserve0;
                var deadWalletBalanceTasks = _sniperConfig.DeadWallets.Select(t => balanceOfFunction.CallAsync<BigInteger>(t)).ToList();
                await Task.WhenAll(deadWalletBalanceTasks);
                BigInteger deadWalletBalance = new BigInteger(0);
                deadWalletBalanceTasks.ForEach(t => deadWalletBalance += t.Result);
                var totalTokenAmount = await ercContract.GetFunction("totalSupply").CallAsync<BigInteger>();
                if (totalTokenAmount == 0)
                {
                    Serilog.Log.Logger.Error("Token {0} contract is giving a invalid supply", token);
                    return false;
                }
                totalAmount -= deadWalletBalance;
                var percentageInPool = new Fraction(tokenAmountInPool).Divide(totalTokenAmount).Multiply(100);
                result = ((decimal)percentageInPool) > _sniperConfig.MinimumPercentageOfTokenInLiquidityPool;
                Serilog.Log.Logger.Information("Token {0} Token Amount in Pool: {1} Total Supply: {2} Burned {3} Total Percentage in pool: {4}% Min Percentage Liquidity Check Status: {5}", token, tokenAmountInPool, totalTokenAmount, deadWalletBalance.ToString(), percentageInPool.ToDouble(), result);
            }
            return result;
        }

        public Task<Reserves> GetReserves(string currentPair)
        {
            var pairContract = _bscWeb3.Eth.GetContract(_pairContractStr, currentPair);
            return pairContract.GetFunction("getReserves").CallDeserializingToObjectAsync<Reserves>();
        }

        public async Task<bool> CheckContractVerified(string otherTokenAddress)
        {
            var result = await _httpClient.GetAsync(string.Format(GetSourceUrl, otherTokenAddress, _sniperConfig.BscScanApikey));
            var jObject = JObject.Parse(await result.Content.ReadAsStringAsync());
            var innerResult = jObject["result"][0];
            if (innerResult["ABI"].Value<string>() == "Contract source code not verified")
            {
                Serilog.Log.Logger.Warning("Bsc contract is not verified for token {0}", otherTokenAddress);
                return false;
            }
            var srcCode = innerResult.Value<string>("SourceCode");

            if (_sniperConfig.CheckRouterAddressInContract)
            {
                if (!srcCode.Contains(_sniperConfig.PancakeswapRouterAddress) && !srcCode.Contains(_sniperConfig.V1PancakeswapRouterAddress))
                {
                    Serilog.Log.Logger.Information("Pancake swap router is invalid for token {0}", otherTokenAddress);
                    return false;
                }
            }

            var containsRugCheckStrings = _sniperConfig.ContractRugCheckStrings.FirstOrDefault(t => srcCode.Contains(t));
            if (!string.IsNullOrEmpty(containsRugCheckStrings))
            {
                Serilog.Log.Logger.Warning("Failed rug check for token {0}, contains string: {1}", otherTokenAddress, containsRugCheckStrings);
                return false;
            }

            return true;
        }
    }
}
