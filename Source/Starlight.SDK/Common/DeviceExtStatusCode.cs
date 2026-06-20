namespace Starlight.SDK.Common;

/// <summary>
/// Status codes returned inside <c>DeviceExtListData.code</c> by the
/// <c>/device-fp/api/getExtList</c> endpoint. These mirror the HTTP-style
/// numeric codes the official client expects.
/// </summary>
public enum DeviceExtStatusCode
{
    /// <summary>Request succeeded.</summary>
    Ok = 200,

    /// <summary>Request rejected (missing/invalid platform parameter).</summary>
    Forbidden = 403
}
