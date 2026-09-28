using System.Runtime.InteropServices;
using System.Text;

namespace DsxLite.Core.Haptics;

/// <summary>Minimal cfgmgr32 interop for walking the device tree.</summary>
public static class CfgMgr
{
    [DllImport("cfgmgr32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string devID, int flags);

    [DllImport("cfgmgr32", ExactSpelling = true)]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, int flags);

    [DllImport("cfgmgr32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int bufferLen, int flags);

    /// <summary>True when the device node currently exists in the device tree.</summary>
    public static bool DevNodeExists(string deviceInstanceId) =>
        CM_Locate_DevNodeW(out _, deviceInstanceId, 0) == 0;

    /// <summary>
    /// Walks up the device tree from <paramref name="deviceInstanceId"/> and returns the
    /// device ID of the first ancestor matching <paramref name="predicate"/>, or null.
    /// </summary>
    public static string? GetAncestorDeviceId(string deviceInstanceId, Func<string, bool> predicate, int maxDepth = 8)
    {
        foreach (string id in GetAncestorDeviceIds(deviceInstanceId, maxDepth))
            if (predicate(id))
                return id;
        return null;
    }

    /// <summary>Returns the device IDs of all ancestors up to <paramref name="maxDepth"/> levels.</summary>
    public static List<string> GetAncestorDeviceIds(string deviceInstanceId, int maxDepth = 8)
    {
        var result = new List<string>();
        if (CM_Locate_DevNodeW(out uint node, deviceInstanceId, 0) != 0)
            return result;

        for (int i = 0; i < maxDepth; i++)
        {
            if (CM_Get_Parent(out node, node, 0) != 0)
                break;
            var buffer = new StringBuilder(512);
            if (CM_Get_Device_IDW(node, buffer, buffer.Capacity, 0) != 0)
                break;
            result.Add(buffer.ToString());
        }
        return result;
    }
}
