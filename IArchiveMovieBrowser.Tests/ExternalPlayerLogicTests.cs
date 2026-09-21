using System;
using System.IO;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class ExternalPlayerLogicTests : IDisposable
{
    private readonly string _tempExe;

    public ExternalPlayerLogicTests()
    {
        _tempExe = Path.Combine(Path.GetTempPath(), "iamb_test_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(_tempExe, "fixture");
    }

    public void Dispose()
    {
        try { File.Delete(_tempExe); } catch { /* best effort */ }
    }

    // --- TryNormalizeForSave: validation rules -------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_RejectsBlank(string? path)
        => Assert.False(ExternalPlayerLogic.TryNormalizeForSave(path, out _));

    [Fact]
    public void TryNormalize_RejectsNonExeExtension()
    {
        string p = Path.Combine(Path.GetTempPath(), "player.bin");
        try
        {
            File.WriteAllText(p, "x");
            Assert.False(ExternalPlayerLogic.TryNormalizeForSave(p, out _));
        }
        finally { try { File.Delete(p); } catch { } }
    }

    [Fact]
    public void TryNormalize_AcceptsUppercaseExeExtension()
    {
        string upper = Path.Combine(Path.GetTempPath(), "iamb_upper_" + Guid.NewGuid().ToString("N") + ".EXE");
        try
        {
            File.WriteAllText(upper, "x");
            Assert.True(ExternalPlayerLogic.TryNormalizeForSave(upper, out string? n));
            Assert.Equal(upper, n);
        }
        finally { try { File.Delete(upper); } catch { } }
    }

    [Theory]
    [InlineData("player.exe")]               // relative
    [InlineData(@".\player.exe")]
    [InlineData(@"..\player.exe")]
    public void TryNormalize_RejectsRelativePath(string rel)
        => Assert.False(ExternalPlayerLogic.TryNormalizeForSave(rel, out _));

    [Fact]
    public void TryNormalize_RejectsDirectory()
    {
        string dir = Path.GetTempPath();
        Assert.False(ExternalPlayerLogic.TryNormalizeForSave(dir, out _));
    }

    [Fact]
    public void TryNormalize_RejectsMissingFile()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(ExternalPlayerLogic.TryNormalizeForSave(missing, out _));
    }

    [Fact]
    public void TryNormalize_AcceptsExistingAbsoluteExe_PathUnchanged()
    {
        Assert.True(ExternalPlayerLogic.TryNormalizeForSave(_tempExe, out string? normalized));
        Assert.Equal(_tempExe, normalized);
    }

    // --- AnalyzeStoredPath / state ------------------------------------------

    [Fact]
    public void Analyze_NoConfiguredPathIsNotConfigured()
    {
        Assert.Equal(PlayerConfigurationState.NotConfigured,
            ExternalPlayerLogic.AnalyzeStoredPath(null).State);
        Assert.Equal(PlayerConfigurationState.NotConfigured,
            ExternalPlayerLogic.AnalyzeStoredPath("  ").State);
    }

    [Fact]
    public void Analyze_ValidExistingExeIsReady()
    {
        var cfg = ExternalPlayerLogic.AnalyzeStoredPath(_tempExe);
        Assert.Equal(PlayerConfigurationState.Ready, cfg.State);
        Assert.Equal(_tempExe, cfg.ValidatedPath);
    }

    [Fact]
    public void Analyze_MissingExeIsMissing_NotSilentlyCleared()
    {
        string missing = Path.Combine(Path.GetTempPath(), "gone_" + Guid.NewGuid().ToString("N") + ".exe");
        var cfg = ExternalPlayerLogic.AnalyzeStoredPath(missing);
        Assert.Equal(PlayerConfigurationState.Missing, cfg.State);
        Assert.Equal(missing, cfg.ValidatedPath); // path preserved for the UI, not cleared
    }

    [Fact]
    public void Analyze_NonAbsoluteOrNonExeIsInvalid()
    {
        Assert.Equal(PlayerConfigurationState.Invalid, ExternalPlayerLogic.AnalyzeStoredPath("player.exe").State);
        Assert.Equal(PlayerConfigurationState.Invalid, ExternalPlayerLogic.AnalyzeStoredPath(Path.GetTempPath()).State);
    }

    // --- Display helpers ----------------------------------------------------

    [Fact]
    public void DisplayFacade_HandlesStates()
    {
        Assert.Equal("External player", PlayerDisplayText.AreaLabel);
        Assert.Equal("Choose player…", PlayerDisplayText.ChooseButtonLabel);
        Assert.Equal("Change…", PlayerDisplayText.ChangeButtonLabel);
        Assert.Equal("Clear", PlayerDisplayText.ClearButtonLabel);

        Assert.Contains("External player ready:", PlayerDisplayText.ReadyStatus(_tempExe));
        Assert.NotEmpty(PlayerDisplayText.MissingStatus);
        Assert.NotEmpty(PlayerDisplayText.InvalidStatus);
        Assert.NotEmpty(PlayerDisplayText.NotConfiguredStatus);
    }

    // --- Persistence abstraction (in-memory fake) ---------------------------

    private sealed class FakePlayerStorage : IPlayerExecutableStorage
    {
        public string? Stored;
        public string? Load() => Stored;
        public void Save(string? path) => Stored = path;
        public void Clear() => Stored = null;
    }

    [Fact]
    public void Persistence_StoresValidPathAndClearRemovesIt()
    {
        var storage = new FakePlayerStorage();
        Assert.Null(storage.Load());

        // Only a validated path is stored by the caller flow: validate first.
        string invalid = "not an exe";
        Assert.False(ExternalPlayerLogic.TryNormalizeForSave(invalid, out _));
        // The caller would not call Save for invalid paths; simulate the accepted flow:
        storage.Save(_tempExe);
        Assert.Equal(_tempExe, storage.Load());
        Assert.Equal(PlayerConfigurationState.Ready, ExternalPlayerLogic.AnalyzeStoredPath(storage.Load()).State);

        storage.Clear();
        Assert.Null(storage.Load());
        Assert.Equal(PlayerConfigurationState.NotConfigured, ExternalPlayerLogic.AnalyzeStoredPath(storage.Load()).State);
    }
}