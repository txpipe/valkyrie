using Cardano.Sync;
using CardanoSharp.Wallet;
using CardanoSharp.Wallet.Enums;
using CardanoSharp.Wallet.Extensions.Models;
using CardanoSharp.Wallet.Extensions.Models.Transactions;
using CardanoSharp.Wallet.Models;
using CardanoSharp.Wallet.Models.Addresses;
using CardanoSharp.Wallet.Models.Derivations;
using CardanoSharp.Wallet.Models.Keys;
using CardanoSharp.Wallet.Utilities;

namespace Valkyrie;

public class Worker(ILogger<Worker> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private List<Utxo>? Utxos { get; set; }
    private HttpClient SubmitApi => httpClientFactory.CreateClient("SubmitApi");
    private readonly string _targetAddress = configuration["TargetAddress"]!;
    private string _address = default!;
    private readonly CardanoNodeClient _client = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _client.ConnectAsync(configuration["CardanoNodeSocketPath"]!, configuration.GetValue<uint>("CardanoNetwork"));

        MnemonicService mnemonicService = new();
        Mnemonic mnemonic = mnemonicService.Restore(configuration["Mnemonic"]!);
        PrivateKey rootKey = mnemonic.GetRootKey();

        // Derive down to our Account Node
        IAccountNodeDerivation accountNode = rootKey.Derive()
            .Derive(PurposeType.Shelley)
            .Derive(CoinType.Ada)
            .Derive(0);

        IIndexNodeDerivation paymentNode = accountNode
            .Derive(RoleType.ExternalChain)
            .Derive(0);

        IIndexNodeDerivation stakeNode = accountNode
            .Derive(RoleType.Staking)
            .Derive(0);

        // Payment Node Keys
        PublicKey vkey = paymentNode.PublicKey;
        PrivateKey skey = paymentNode.PrivateKey;

        // Staking Node Keys
        PublicKey stakeVkey = stakeNode.PublicKey;

        _address = AddressUtility.GetBaseAddress(vkey, stakeVkey, Utils.GetNetworkType(configuration)).ToString();

        _logger.LogInformation("Address: {address}", _address);

        // Update States
        await UpdateUtxosAsync();

        int retry = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // If mempool is not full, send a transaction && utxos is not null
            if (Utxos is not null)
            {
                try
                {
                    var (txBytes, txHash, consumedUtxos, changeUtxos) = Utils.BuildTx(
                        _address,
                        vkey,
                        skey,
                        _targetAddress,
                        1_000_000,
                        Utxos
                    );

                    // Submit transaction
                    // Execute the POST request
                    ByteArrayContent submitPayload = new(txBytes);
                    submitPayload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/cbor");
                    HttpResponseMessage submitTxResponse = await SubmitApi.PostAsync("api/submit/tx", submitPayload, stoppingToken);

                    if (!submitTxResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Error while submitting transaction. Status Code: {submitTxResponse.StatusCode}. Response: {txHash}");
                    }

                    _logger.LogInformation("Transaction submitted: {txHash}", txHash);
                    // If transaction is successful, update utxos
                    Utxos = Utxos.Except(consumedUtxos).Concat(changeUtxos).ToList();
                    retry = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send transaction");

                    if (retry++ >= 5)
                    {
                        await UpdateUtxosAsync();
                        retry = 0;
                    }
                    // Wait for a while before trying again
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
    }

    private async Task UpdateUtxosAsync()
    {
        try
        {
            var utxosByAddress = await _client.GetUtxosByAddressAsync(_address);
            Utxos = utxosByAddress.Values.Select(uba => Utils.MapUtxoByAddressToUtxo(uba.Key.Value, uba.Value.Value)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update utxos");
        }
    }
}
