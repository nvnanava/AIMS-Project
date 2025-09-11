namespace AIMS.Utilities;

public static class CacheStamp
{
    private static long _assetsVersion = 0;
    public static long AssetsVersion => Interlocked.Read(ref _assetsVersion);
    public static void BumpAssets() => Interlocked.Increment(ref _assetsVersion);
}
