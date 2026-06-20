namespace Starlight.SDK.Common;

/// <summary>
/// Channel identifiers sent in the <c>channel_id</c> field of combo
/// granter requests. The wire format is the integer value.
/// </summary>
public enum ChannelId
{
    /// <summary>
    /// Official / default channel. When the request carries this value
    /// the server reports <c>modified=false</c> on
    /// <c>compareProtocolVersion</c> and skips the protocol payload.
    /// </summary>
    Official = 1
}
