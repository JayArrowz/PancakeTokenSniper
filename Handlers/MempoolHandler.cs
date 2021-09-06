using BscTokenSniper.Models;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Eth.Filters;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BscTokenSniper.Handlers
{
    public class MempoolHandler
    {
        private readonly SniperConfiguration _sniperConfig;
        private readonly Web3 _bscWeb3;

        public MempoolHandler(IOptions<SniperConfiguration> options)
        {
            _sniperConfig = options.Value;
            _bscWeb3 = new Web3(url: _sniperConfig.BscHttpApi, account: new Account(_sniperConfig.WalletPrivateKey, new BigInteger(_sniperConfig.ChainId)));
            _bscWeb3.TransactionManager.UseLegacyAsDefault = true;
        }

        private async Task<Transaction> GetTransactionAsync(string txHash)
        {
            var result = await _bscWeb3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash);
            return result;
        }

        public async Task Add(string t)
        {
            var transaction = await GetTransactionAsync(t);
            if (transaction != null && (transaction.To == _sniperConfig.V1PancakeswapRouterAddress || transaction.To == _sniperConfig.PancakeswapRouterAddress))
            {
                var isRemoveLiquidity = transaction.IsTransactionForFunctionMessage<RemoveLiquidityWithPermitFunction>();
                if (isRemoveLiquidity)
                {
                    var decodedEvent = transaction.DecodeTransactionToFunctionMessage<RemoveLiquidityWithPermitFunction>();
                    Serilog.Log.Logger.Information("Remove liquidity event detected {@decodedEvent}", decodedEvent);
                }
            }
        }
    }
}
