namespace MapCore.Tests;

static class SetupTests
{
    [Test]
    public static void TestMapCopyIsPresent()
    {
        var path = TestData.PathOf(TestData.AcidicQuarryArchive);
        Assert.True(File.Exists(path), $"Missing test map {path}. Run build.ps1 to create it.");
        Assert.Equal(50_602_762L, new FileInfo(path).Length, "test map size");
    }

    [Test]
    public static void SevenZipIsAvailable()
    {
        Assert.True(Tools.FindSevenZip() != null, "7-Zip not found; .sd7 archives cannot be read or written");
    }
}
