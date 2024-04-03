
using CardanoSharp.Wallet.CIPs.CIP2;
using CardanoSharp.Wallet.CIPs.CIP2.ChangeCreationStrategies;
using CardanoSharp.Wallet.CIPs.CIP2.Models;
using CardanoSharp.Wallet.Extensions;
using CardanoSharp.Wallet.Extensions.Models.Transactions;
using CardanoSharp.Wallet.Models;
using CardanoSharp.Wallet.Models.Addresses;
using CardanoSharp.Wallet.Models.Keys;
using CardanoSharp.Wallet.Models.Transactions;
using CardanoSharp.Wallet.TransactionBuilding;
using CardanoSharp.Wallet.Utilities;
using CardanoSharp.Wallet.CIPs.CIP2.Extensions;
using CardanoSharp.Wallet.Enums;

namespace Valkyrie;
public static class Utils
{
    // Returns the serialized transaction, consumed utxos and change utxos
    public static (byte[], string, List<Utxo>, List<Utxo>) BuildTx(string changeAddress, PublicKey vkey, PrivateKey skey, string toAddress, ulong amount, List<Utxo> utxos)
    {
        // Create a transaction output
        TransactionOutputValue outputValue = new() { Coin = amount, MultiAsset = [] };
        TransactionOutput txOutputTarget = TransactionOutputBuilder.Create
            .SetAddress(new Address(toAddress).GetBytes())
            .SetTransactionOutputValue(outputValue)
            .Build();

        var changeAsset = utxos.AggregateAssets();
        TransactionOutput txOutputChange = TransactionOutputBuilder.Create
            .SetAddress(new Address(changeAddress).GetBytes())
            .SetTransactionOutputValue(
                new TransactionOutputValue { Coin = changeAsset.Lovelaces - amount, MultiAsset = ConvertBalanceAssetToNativeAsset([.. changeAsset.Assets]) })
            .Build();

        // Build the transaction body
        ITransactionBodyBuilder txBodyBuilder = TransactionBodyBuilder.Create;

        utxos.ForEach(utxo => txBodyBuilder.AddInput(utxo));
        txBodyBuilder.AddOutput(txOutputTarget);
        txBodyBuilder.AddOutput(txOutputChange);

        // Build the transaction
        ITransactionBuilder txBuilder = TransactionBuilder.Create;
        txBuilder.SetBody(txBodyBuilder);

        VKeyWitness vkeyWitness = new()
        {
            VKey = vkey,
            SKey = skey,
        };
        txBuilder.SetWitnesses(TransactionWitnessSetBuilder.Create.AddVKeyWitness(vkeyWitness));
        Transaction tx = txBuilder.Build();

        // Set fee
        uint fee = tx.CalculateAndSetFee();
        tx.TransactionBody.TransactionOutputs.Last().Value.Coin -= fee;
        string txHash = Convert.ToHexString(HashUtility.Blake2b256(tx.TransactionBody.GetCBOR(null).EncodeToBytes())).ToLowerInvariant();

        // Return all the outputs going back to the change address
        var changeOutputs = tx.TransactionBody.TransactionOutputs.Where(output => new Address(output.Address).ToString() == changeAddress).ToList();
        var changeUtxos = changeOutputs.Select(output => new Utxo()
        {
            TxHash = txHash,
            TxIndex = (uint)tx.TransactionBody.TransactionOutputs.IndexOf(output),
            OutputAddress = changeAddress,
            OutputDatumOption = output.DatumOption,
            OutputScriptReference = output.ScriptReference,
            Balance = new Balance()
            {
                Lovelaces = output.Value.Coin,
                Assets = ConvertNativeAssetToBalanceAsset(output.Value.MultiAsset)
            }
        }).ToList();

        return (tx.Serialize(), txHash, utxos, changeUtxos);
    }

    public static CoinSelection GetCoinSelection(
        IEnumerable<TransactionOutput> outputs,
        IEnumerable<Utxo> utxos, string changeAddress,
        ITokenBundleBuilder? mint = null,
        List<Utxo>? requiredUtxos = null,
        int limit = 20, ulong feeBuffer = 0uL)
    {
        OptimizedRandomImproveStrategy coinSelectionStrategy = new();
        SingleTokenBundleStrategy changeCreationStrategy = new();
        CoinSelectionService coinSelectionService = new(coinSelectionStrategy, changeCreationStrategy);
        CoinSelection result = coinSelectionService.GetCoinSelection(outputs, utxos, changeAddress, mint, requiredUtxos, limit, feeBuffer);

        return result;
    }

    public static List<Asset> ConvertNativeAssetToBalanceAsset(Dictionary<byte[], NativeAsset> multiAsset)
    {
        if (multiAsset == null)
        {
            return [];
        }

        List<Asset> balanceAssets = [];
        multiAsset.Keys.ToList().ForEach((policyId) =>
        {
            string policyIdHex = Convert.ToHexString(policyId).ToLowerInvariant();
            var asset = multiAsset[policyId];
            asset.Token.Keys.ToList().ForEach(assetName =>
            {
                string assetNameHex = Convert.ToHexString(assetName).ToLowerInvariant();
                ulong amount = (ulong)asset.Token[assetName];
                balanceAssets.Add(new Asset
                {
                    PolicyId = policyIdHex,
                    Name = assetNameHex,
                    Quantity = (long)amount
                });
            });
        });

        return balanceAssets;
    }

    public static Dictionary<byte[], NativeAsset> ConvertBalanceAssetToNativeAsset(List<Asset> balanceAssets)
    {
        Dictionary<byte[], NativeAsset> multiAsset = [];
        balanceAssets.ForEach(asset =>
        {
            byte[] policyId = Convert.FromHexString(asset.PolicyId);
            byte[] assetName = Convert.FromHexString(asset.Name);
            if (!multiAsset.TryGetValue(policyId, out NativeAsset? value))
            {
                value = new NativeAsset();
                multiAsset[policyId] = value;
            }

            value.Token[assetName] = asset.Quantity;
        });

        return multiAsset;
    }

    public static Utxo MapUtxoByAddressToUtxo(TransactionInput input, TransactionOutput output)
    {
        var utxo = new Utxo
        {
            Balance = new Balance(),
            TxHash = input.TransactionId.ToStringHex(),
            TxIndex = input.TransactionIndex,
            OutputAddress = new Address(output.Address).ToString()
        };
        utxo.Balance.Lovelaces = output.Value.Coin;
        if (output.Value.MultiAsset != null && output.Value.MultiAsset.Count > 0)
        {
            utxo.Balance.Assets = output.Value.MultiAsset
                .SelectMany(
                    x =>
                        x.Value.Token.Select(
                            y =>
                                new Asset()
                                {
                                    PolicyId = x.Key.ToStringHex(),
                                    Name = y.Key.ToStringHex(),
                                    Quantity = y.Value
                                }
                        )
                )
                .ToList();
        }
        return utxo;
    }

    public static NetworkType GetNetworkType(IConfiguration configuration)
    {
        return configuration.GetValue<int>("CardanoNetwork") switch
        {
            764824073 => NetworkType.Mainnet,
            1 => NetworkType.Preprod,
            2 => NetworkType.Preview,
            _ => throw new NotImplementedException()
        };
    }
}