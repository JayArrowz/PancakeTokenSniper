# PancakeSwap Token Sniper
BSC BNB Pancake token sniper
The BSC token sniper listens for new blocks on BSC relating to the pancake swap factory contract. The `createPair` log event is emitted on the pancake swap factory whenever a new liquidity pool is added. The sniper will filter the logs from these blocks to find these `createPair` events. Then with the correct RUG checks it will buy the token, after a certain amount of profit has been made it will automatically sell the token. The token sniper will also listen to the chain and filter out transactions which call the function `addLiquidityETH` these events will be sniped and go through the relevent checks.

## Donations
This is a free project but any funding is appricated.
ETH/BNB: 0x71f74dEbb7fd42E61de32256537284E06DE8812d

## Socials
Telegram: https://t.me/PancakeTokenSniper

## Prerequisites
* [Net5.0](https://dotnet.microsoft.com/download/dotnet/5.0) (Only need this if you are trying to run the code otherwise please see [releases](https://github.com/JayArrowz/PancakeTokenSniper/releases) it provides binaries)

## Config
The Config is listed in `appsettings.json` There are values which you will have to set yourself, these are denoted with `xxx`.

### Bsc Node and Http Api
Inside the config you will see `BscHttpApi` and `BscNode` keys. 
Both are obtained from https://moralis.io for free. You will have to navigate to Speedy Notes and click endpoints on the Bsc Network.
![image](https://user-images.githubusercontent.com/49910176/131349328-cabed516-2718-4afd-97d3-e16961c7c83f.png)

Remember `BscNode` should be WS mainnet Endpoints and `BscHttpApi` should be Http endpoints
![image](https://user-images.githubusercontent.com/49910176/131349432-a4768c58-526c-407e-8cf6-547e1aacebf5.png)

### Bsc Scan API Key
The BSC Scan API key is obtained for free from https://bscscan.com/myapikey

## Running the project
If you want to run the project you can go to [releases](https://github.com/JayArrowz/PancakeTokenSniper/releases) and a binary that will execute on your OS, or install Net5.0 then compile and run the application. You can use `dotnet run BscTokenSniper` to do this.

## Rug Checks
There are specific checks involved that this sniper does when buying tokens. Some config values in appsettings.json inside `SniperConfiguration` are used to influence the rug checks. You can disable the Rug checks by setting the `RugCheckEnabled` to false. Be warned this is dangerous.

1. Minimum % total supply in the liquidty pool. The config relating this value is called `MinimumPercentageOfTokenInLiquidityPool`. A example of this is if you want the token ZZZ to have atleast 90 percent of its supply inside the liqudity pool you can set this value to 90.
2. The ability to scan contract source code and exclude buying from contracts when they include a specific string. The config key is called `ContractRugCheckStrings`
3. Minimum amount of BNB (This can be changed to any other token by using `LiquidityPairAddress`) inside liquidity pool. The config key is called `MinLiquidityAmount`. 
4. Ensures liquidty pair created has one of the `LiquidityPairAddress` address
5. If `HoneypotCheck` is true the sniper will try to buy the `HoneypotCheckAmount`, then it will try to sell it. If this operation is successful it will buy the `SnipeAmount`
6. If `RugdocCheckEnabled` is true the sniper will use the Rugdoc honeypot checker to check the contract
7. The `WhitelistedTokens` can be used to bypass any honeypot and rug checks. This field accepts multiple token addresses
8. If the `CheckRouterAddressInContract` is enabled the sniper will check if the contract contains the router address 
9. If the `OnlyBuyWhitelist` is enabled the contract will only interact with whitelisted tokens

## Buying
The amount to snipe is denoted in the config as `AmountToSnipe`. This value will trade the `LiquidityPairAddress` in your wallet with the coin to snipe at the current trade price.
You can set a delay on the buying buy setting the value `BuyDelaySeconds` to greater than 0

## Selling
The Sniper automatically sells once a certain percentage of profit is made. This is defined in the config key `ProfitPercentageMargin`

## Contributing

Please read [CONTRIBUTING.md](https://gist.github.com/PurpleBooth/b24679402957c63ec426) for details on our code of conduct, and the process for submitting pull requests to us.

## Versioning

We use [SemVer](http://semver.org/) for versioning. For the versions available, see the [tags on this repository](https://github.com/JayArrowz/PancakeTokenSniper/tags). 

## Authors

* **JayArrowz** - [JayArrowz](https://github.com/JayArrowz)

See also the list of [contributors](https://github.com/JayArrowz/PancakeTokenSniper/contributors) who participated in this project.

## TODO
- Support Uniswap and other liquidity providers
- Persist bought assets on Postgres
- Target specific token addresses & detect when locked
- Check for Renounce ownership
