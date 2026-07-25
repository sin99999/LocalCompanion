using LocalCompanion;

namespace LocalCompanion.Core.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData(@"D:\bin\LocalCompanion\", false)]
    [InlineData(@"C:\work\bin\tools", false)]
    [InlineData(@"C:\Users\sample\Downloads\bin\x64\LocalCompanion\", false)]
    [InlineData(@"C:\src\MyApp\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\", true)]
    [InlineData(@"C:\src\MyApp\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\", true)]
    [InlineData(@"C:\src\MyApp\bin\Release\net10.0\win-x64\", true)]
    [InlineData(@"C:\src\MyApp\obj\Debug\", true)]
    public void IsDevelopmentOutputPath_ClassifiesBuildOutputs(string path, bool expectedDev)
    {
        Assert.Equal(expectedDev, AppPaths.IsDevelopmentOutputPath(path));
    }

    [Fact]
    public void FindDistributionRoot_FindsFolderWithScriptsAndModels()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "LocalCompanion.csproj")))
        {
            var parent = Directory.GetParent(root);
            Assert.NotNull(parent);
            root = parent!.FullName;
        }

        var found = AppPaths.FindDistributionRoot(root, root);
        Assert.True(Directory.Exists(Path.Combine(found, "scripts")));
        Assert.True(Directory.Exists(Path.Combine(found, "models")));
    }
}
