using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlexLyricSync.Core.Utils;

namespace PlexLyricSync.IntegrationTests;

[TestClass]
public abstract class IntegrationTestsBase
{
    private string TestFilesPath { get; set; } = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        TestFilesPath = RequireEnv("TEST_FILES_PATH", "Set TEST_FILES_PATH to run this integration test.");
        Directory.CreateDirectory(TestFilesPath);
        LrcParser.SetPathProvider(new TestPathProvider(TestFilesPath));
    }

    private static string RequireEnv(string name, string inconclusiveMessage)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive(inconclusiveMessage);

        return value!;
    }
}
