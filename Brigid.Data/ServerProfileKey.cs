namespace Brigid.Data;

/// <summary>
///     Resolves the stable, filesystem-safe key that scopes a character's config to the server it belongs to
///     (<c>profiles/{serverKey}/{character}</c>). This is the single home for "which server is this" classification —
///     do not re-derive retail/Hybrasyl detection elsewhere; route it through here.
/// </summary>
/// <remarks>
///     v0 relies on the ad-hoc signals available today (connected host, server name). The proper structured
///     server-identity model (a future <c>ConnectionManager.ServerType</c>/id) will supersede this aliasing; when it
///     lands, this resolver is the one place to update.
/// </remarks>
public static class ServerProfileKey
{
    public const string RetailKey = "kru";
    public const string HybrasylKey = "hybrasyl";
    private const string UnknownKey = "unknown";
    private const int MaxSlugLength = 64;

    /// <summary>True when the host belongs to retail Dark Ages (KRU-operated: <c>kru.com</c> or any <c>*.kru.com</c>).</summary>
    public static bool IsRetail(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && (host.Equals("kru.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".kru.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Resolves the profile key for the connected server. Retail (<c>*.kru.com</c>) → <c>kru</c>; Hybrasyl →
    ///     <c>hybrasyl</c>; anything else → a filesystem-safe slug of the host (falling back to the server name, then
    ///     <c>unknown</c>).
    /// </summary>
    public static string Resolve(string? host, string? serverName)
    {
        if (IsRetail(host))
            return RetailKey;

        if (IsHybrasyl(host, serverName))
            return HybrasylKey;

        var slug = Sanitize(FirstNonBlank(host, serverName));

        return slug.Length > 0 ? slug : UnknownKey;
    }

    private static bool IsHybrasyl(string? host, string? serverName) =>
        Contains(host, "hybrasyl") || Contains(serverName, "hybrasyl");

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value;

        return null;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var mapped = value.Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || (c == '-') ? c : '_');

        var slug = new string(mapped.ToArray()).Trim('_');

        //truncate over-long slugs, then re-trim so the cut can't leave a trailing '_'
        return slug.Length > MaxSlugLength ? slug[..MaxSlugLength].Trim('_') : slug;
    }
}
