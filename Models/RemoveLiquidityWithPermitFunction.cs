using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BscTokenSniper.Models
{
    public partial class RemoveLiquidityWithPermitFunction : RemoveLiquidityWithPermitFunctionBase { }

    [Function("removeLiquidityWithPermit", typeof(RemoveLiquidityWithPermitOutputDTO))]
    public class RemoveLiquidityWithPermitFunctionBase : FunctionMessage
    {
        [Parameter("address", "tokenA", 1)]
        public virtual string TokenA { get; set; }
        [Parameter("address", "tokenB", 2)]
        public virtual string TokenB { get; set; }
        [Parameter("uint256", "liquidity", 3)]
        public virtual BigInteger Liquidity { get; set; }
        [Parameter("uint256", "amountAMin", 4)]
        public virtual BigInteger AmountAMin { get; set; }
        [Parameter("uint256", "amountBMin", 5)]
        public virtual BigInteger AmountBMin { get; set; }
        [Parameter("address", "to", 6)]
        public virtual string To { get; set; }
        [Parameter("uint256", "deadline", 7)]
        public virtual BigInteger Deadline { get; set; }
        [Parameter("bool", "approveMax", 8)]
        public virtual bool ApproveMax { get; set; }
        [Parameter("uint8", "v", 9)]
        public virtual byte V { get; set; }
        [Parameter("bytes32", "r", 10)]
        public virtual byte[] R { get; set; }
        [Parameter("bytes32", "s", 11)]
        public virtual byte[] S { get; set; }
    }

    public partial class RemoveLiquidityWithPermitOutputDTO : RemoveLiquidityWithPermitOutputDTOBase { }

    [FunctionOutput]
    public class RemoveLiquidityWithPermitOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint256", "amountA", 1)]
        public virtual BigInteger AmountA { get; set; }
        [Parameter("uint256", "amountB", 2)]
        public virtual BigInteger AmountB { get; set; }
    }
}
