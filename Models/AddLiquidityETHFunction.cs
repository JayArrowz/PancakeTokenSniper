using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace BscTokenSniper.Models
{
    public partial class AddLiquidityETHFunction : AddLiquidityETHFunctionBase { }

    [Function("addLiquidityETH", typeof(AddLiquidityETHOutputDTO))]
    public class AddLiquidityETHFunctionBase : FunctionMessage
    {
        [Parameter("address", "token", 1)]
        public virtual string Token { get; set; }
        [Parameter("uint256", "amountTokenDesired", 2)]
        public virtual BigInteger AmountTokenDesired { get; set; }
        [Parameter("uint256", "amountTokenMin", 3)]
        public virtual BigInteger AmountTokenMin { get; set; }
        [Parameter("uint256", "amountETHMin", 4)]
        public virtual BigInteger AmountETHMin { get; set; }
        [Parameter("address", "to", 5)]
        public virtual string To { get; set; }
        [Parameter("uint256", "deadline", 6)]
        public virtual BigInteger Deadline { get; set; }
    }

    public partial class AddLiquidityETHOutputDTO : AddLiquidityETHOutputDTOBase { }

    [FunctionOutput]
    public class AddLiquidityETHOutputDTOBase : IFunctionOutputDTO
    {
        [Parameter("uint256", "amountToken", 1)]
        public virtual BigInteger AmountToken { get; set; }
        [Parameter("uint256", "amountETH", 2)]
        public virtual BigInteger AmountETH { get; set; }
        [Parameter("uint256", "liquidity", 3)]
        public virtual BigInteger Liquidity { get; set; }
    }
}
