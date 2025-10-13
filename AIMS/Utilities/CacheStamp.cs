namespace AIMS.Utilities;

using System.Threading;
using Microsoft.Extensions.Primitives;

public static class CacheStamp
{
    private static long _assetsVersion = 0;
    private static CancellationTokenSource _assetsCts = new();

    public static long AssetsVersion => Interlocked.Read(ref _assetsVersion);

    public static IChangeToken GetAssetsChangeToken()
        => new CancellationChangeToken(_assetsCts.Token);

    public static void BumpAssets()
    {
        Interlocked.Increment(ref _assetsVersion);

        var old = Interlocked.Exchange(ref _assetsCts, new CancellationTokenSource());
        try { old.Cancel(); } catch { /* ignore */ }
        old.Dispose();
    }
}
