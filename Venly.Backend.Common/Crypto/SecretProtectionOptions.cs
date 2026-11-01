using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Venly.Backend.Common.Crypto;

/// <summary>
/// Key-encryption keys for <see cref="AesGcmSecretProtector"/>, bound from a configuration section (Authet uses
/// <c>EngineKeys</c>): <c>EncryptionKeys</c> maps a KEK id to a base64-encoded 32-byte key, and
/// <c>ActiveKeyId</c> names the one new secrets are sealed with. Older ids stay listed so their rows still open.
/// The values are injected at runtime (environment / secret store), never committed.
/// </summary>
public sealed class SecretProtectionOptions
{
    public const string DefaultSectionName = "EngineKeys";

    /// <summary>The marker every committed placeholder value starts with.</summary>
    public const string PlaceholderPrefix = "NOT_A_SECRET__";

    public Dictionary<string, string> EncryptionKeys { get; set; } = new(StringComparer.Ordinal);

    public string ActiveKeyId { get; set; } = string.Empty;
}

/// <summary>
/// Fails startup (ValidateOnStart) unless the active KEK id is set and every KEK is a real base64 32-byte key.
/// A committed placeholder is refused — including in Development — with a message that names the environment
/// variable to set, rather than silently generating a key that would make existing ciphertext unreadable.
/// </summary>
public sealed class SecretProtectionOptionsValidator(string sectionName = SecretProtectionOptions.DefaultSectionName)
    : IValidateOptions<SecretProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretProtectionOptions options)
    {
        var failures = Check(options, sectionName);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static List<string> Check(SecretProtectionOptions options, string section)
    {
        var failures = new List<string>();
        var active = options.ActiveKeyId?.Trim();
        if (string.IsNullOrEmpty(active))
        {
            failures.Add($"{section}:ActiveKeyId is required.");
        }
        else if (!options.EncryptionKeys.ContainsKey(active))
        {
            failures.Add(
                $"{section}:EncryptionKeys has no entry for the active key id '{active}'. "
                + $"Set {section}__EncryptionKeys__{active} to a base64-encoded 32-byte key "
                + "(e.g. `openssl rand -base64 32`).");
        }

        foreach (var (id, value) in options.EncryptionKeys)
        {
            var hint = $"Set the environment variable {section}__EncryptionKeys__{id} to a base64-encoded 32-byte "
                + "key (generate one with `openssl rand -base64 32`). It is injected at runtime and never committed.";
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v))
            {
                failures.Add($"{section}:EncryptionKeys:{id} is empty. {hint}");
                continue;
            }
            if (v.StartsWith(SecretProtectionOptions.PlaceholderPrefix, StringComparison.Ordinal))
            {
                failures.Add($"{section}:EncryptionKeys:{id} is still the committed placeholder. {hint}");
                continue;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(v);
            }
            catch (FormatException)
            {
                failures.Add($"{section}:EncryptionKeys:{id} is not valid base64. {hint}");
                continue;
            }
            if (bytes.Length != AesGcmSecretProtector.KeySize)
                failures.Add(
                    $"{section}:EncryptionKeys:{id} decodes to {bytes.Length} bytes; exactly "
                    + $"{AesGcmSecretProtector.KeySize} are required. {hint}");
        }
        return failures;
    }
}

public static class SecretProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISecretProtector"/> (singleton AES-256-GCM) with its KEKs bound from
    /// <paramref name="sectionName"/> and validated on start.
    /// </summary>
    public static IServiceCollection AddSecretProtector(
        this IServiceCollection services, IConfiguration configuration,
        string sectionName = SecretProtectionOptions.DefaultSectionName)
    {
        services.AddOptions<SecretProtectionOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SecretProtectionOptions>>(
            new SecretProtectionOptionsValidator(sectionName)));
        services.TryAddSingleton<ISecretProtector, AesGcmSecretProtector>();
        return services;
    }
}
