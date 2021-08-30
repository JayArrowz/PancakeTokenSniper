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

namespace BscTokenSniper
{
    public class RugChecker
    {
        private readonly string GetSourceUrl = "https://api.bscscan.com/api?module=contract&action=getsourcecode&address={0}&apikey={1}";
        private HttpClient _httpClient;
        private SniperConfiguration _sniperConfig;
        private readonly string _erc20Abi;
        private readonly Web3 _bscWeb3;
        private readonly string _pairContractStr;

        public RugChecker(IOptions<SniperConfiguration> sniperConfig, IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _sniperConfig = sniperConfig.Value;
            _erc20Abi = File.ReadAllText("./Abis/Erc20.json");
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey));
            _pairContractStr = File.ReadAllText("./Abis/Pair.json");
        }

        public async Task<bool> CheckRugAsync(PairCreatedEvent pairCreatedEvent)
        {
            if (pairCreatedEvent.Token0 != _sniperConfig.LiquidityPairAddress && pairCreatedEvent.Token1 != _sniperConfig.LiquidityPairAddress)
            {
                Serilog.Log.Logger.Information("Target liquidity pair found for pair: {0} - {1}. Not bought", pairCreatedEvent.Token0, pairCreatedEvent.Token0);
                return false;
            }

            var otherPairAddress = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ?
                pairCreatedEvent.Token1 : pairCreatedEvent.Token0;
            var otherPairIdx = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ?
                1 : 0;
            Task<bool>[] rugCheckerTasks = new Task<bool>[] {
                CheckContractVerified(otherPairAddress),
                CheckMinLiquidity(pairCreatedEvent, otherPairAddress, otherPairIdx)
            };
            await Task.WhenAll(rugCheckerTasks);
            return rugCheckerTasks.All(t => t.IsCompletedSuccessfully && t.Result);
        }

        private async Task<bool> CheckMinLiquidity(PairCreatedEvent pairCreatedEvent, string pair, int otherPairIdx)
        {
            var currentPair = pairCreatedEvent.Pair;
            var reserves = await GetReserves(currentPair);
            var totalAmount = pairCreatedEvent.Token0.Equals(_sniperConfig.LiquidityPairAddress, StringComparison.InvariantCultureIgnoreCase) ? reserves.Reserve0 : reserves.Reserve1;
                        
            var result = totalAmount >= Web3.Convert.ToWei(_sniperConfig.MinLiquidityAmount);
            var amountStr = Web3.Convert.FromWei(totalAmount).ToString();
            if (!result)
            {
                Serilog.Log.Logger.Information("Not enough liquidity added to token {0}. Not buying. Only {1} liquidity added", pair, amountStr);
                return result;
            }
            else
            {
                Serilog.Log.Logger.Information("Min Liqudity check for {0} token passed. Liqudity amount: {1}", currentPair, amountStr);
            }

            if(_sniperConfig.MinimumPercentageOfTokenInLiquidityPool > 0)
            {
                var tokenAmountInPool = otherPairIdx == 1 ? reserves.Reserve1 : reserves.Reserve0;
                var totalTokenAmount = await _bscWeb3.Eth.GetContract(_erc20Abi, pairCreatedEvent.Pair).GetFunction("totalSupply").CallAsync<BigInteger>();
                var percentageInPool = new Fraction(tokenAmountInPool).Divide(totalTokenAmount).Multiply(100);
                result = ((decimal)percentageInPool) > _sniperConfig.MinimumPercentageOfTokenInLiquidityPool;
                Serilog.Log.Logger.Information("Token {0} Token Amount in Pool: {1} Total Supply: {2} Total Percentage in pool: {3}% Min Percentage Liquidity Check Status: {4}", pairCreatedEvent.Pair, tokenAmountInPool, totalTokenAmount, percentageInPool, result);
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
                Serilog.Log.Logger.Information("Bsc contract is not verified for token {0}", otherTokenAddress);
                return false;
            }

            /*if (!innerResult["SourceCode"].Value<string>().Contains(_sniperConfig.PancakeswapRouterAddress))
            {
                Serilog.Log.Logger.Information("Pancake swap router is invalid for token {0}", otherTokenAddress);
                return false;
            }*/

            var containsRugCheckStrings = _sniperConfig.ContractRugCheckStrings.FirstOrDefault(t => innerResult["SourceCode"].Contains(t));
            if (!string.IsNullOrEmpty(containsRugCheckStrings))
            {
                Serilog.Log.Logger.Information("Failed rug check for token {0}, contains string: {1}", otherTokenAddress, containsRugCheckStrings);
                return false;
            }

            return true;
        }
    }
}
