using Xunit;

namespace SharpAgent.Core.Tests;

public class AgentsMdLoaderTests
{
    [Fact]
    public void GetGlobalAgentsMdPath_ReturnsCodexHomePath()
    {
        // Clear any existing env var
        var originalValue = Environment.GetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar, null);
            
            var path = AgentsMdLoader.GetGlobalAgentsMdPath();
            
            var expectedHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                AgentsMdLoader.DefaultCodexHome);
            var expected = Path.Combine(expectedHome, AgentsMdLoader.FileName);
            
            Assert.Equal(expected, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar, originalValue);
        }
    }

    [Fact]
    public void GetGlobalAgentsMdPath_UsesEnvVarWhenSet()
    {
        var originalValue = Environment.GetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar);
        try
        {
            var customHome = "/custom/codex/home";
            Environment.SetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar, customHome);
            
            var path = AgentsMdLoader.GetGlobalAgentsMdPath();
            
            Assert.Equal(Path.Combine(customHome, AgentsMdLoader.FileName), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AgentsMdLoader.CodexHomeEnvVar, originalValue);
        }
    }

    [Fact]
    public void FindGitRoot_WhenInGitRepo_ReturnsRoot()
    {
        // Use the actual project directory which should be in a git repo
        var currentDir = Directory.GetCurrentDirectory();
        var gitRoot = AgentsMdLoader.FindGitRoot(currentDir);
        
        // Should find a git root (this test runs from the project)
        Assert.NotNull(gitRoot);
        Assert.True(Directory.Exists(Path.Combine(gitRoot, ".git")));
    }

    [Fact]
    public void FindGitRoot_WhenNotInGitRepo_ReturnsNull()
    {
        // Use system temp directory which should not be in a git repo
        var tempDir = Path.GetTempPath();
        var gitRoot = AgentsMdLoader.FindGitRoot(tempDir);
        
        // Temp directory is typically not in a git repo
        // Note: This could fail if temp is somehow inside a git repo
        Assert.Null(gitRoot);
    }

    [Fact]
    public void FindGitRoot_WithNullDirectory_ReturnsNull()
    {
        var gitRoot = AgentsMdLoader.FindGitRoot(null);
        Assert.Null(gitRoot);
    }

    [Fact]
    public void CollectAgentsMdPaths_WithProjectRoot_WalksDown()
    {
        var projectRoot = "/project";
        var workingDir = Path.Combine(projectRoot, "src", "lib");
        
        var paths = AgentsMdLoader.CollectAgentsMdPaths(workingDir, projectRoot).ToList();
        
        Assert.Equal(3, paths.Count);
        Assert.Equal((Path.Combine(projectRoot, "AGENTS.md"), "."), paths[0]);
        Assert.Equal((Path.Combine(projectRoot, "src", "AGENTS.md"), "src"), paths[1]);
        Assert.Equal((Path.Combine(projectRoot, "src", "lib", "AGENTS.md"), Path.Combine("src", "lib")), paths[2]);
    }

    [Fact]
    public void CollectAgentsMdPaths_WithoutProjectRoot_OnlyChecksCwd()
    {
        var workingDir = "/some/directory";
        
        var paths = AgentsMdLoader.CollectAgentsMdPaths(workingDir, null).ToList();
        
        Assert.Single(paths);
        Assert.Equal((Path.Combine(workingDir, "AGENTS.md"), "."), paths[0]);
    }

    [Fact]
    public void CollectAgentsMdPaths_WhenCwdIsProjectRoot_ReturnsSinglePath()
    {
        var projectRoot = "/project";
        
        var paths = AgentsMdLoader.CollectAgentsMdPaths(projectRoot, projectRoot).ToList();
        
        Assert.Single(paths);
        Assert.Equal((Path.Combine(projectRoot, "AGENTS.md"), "."), paths[0]);
    }

    [Fact]
    public void CollectAgentsMdPaths_WithNullWorkingDir_ReturnsEmpty()
    {
        var paths = AgentsMdLoader.CollectAgentsMdPaths(null, "/project").ToList();
        Assert.Empty(paths);
    }

    [Fact]
    public void BuildSystemPrompt_WithContent_CombinesCorrectly()
    {
        var basePrompt = "You are a helpful assistant.";
        var agentsMdContent = "# Project\n\nBuild with: dotnet build";
        
        var result = AgentsMdLoader.BuildSystemPrompt(basePrompt, agentsMdContent);
        
        Assert.Contains(basePrompt, result);
        Assert.Contains("<agents_md>", result);
        Assert.Contains(agentsMdContent, result);
        Assert.Contains("</agents_md>", result);
    }

    [Fact]
    public void BuildSystemPrompt_WithNullContent_ReturnsBasePrompt()
    {
        var basePrompt = "You are a helpful assistant.";
        
        var result = AgentsMdLoader.BuildSystemPrompt(basePrompt, null);
        
        Assert.Equal(basePrompt, result);
    }

    [Fact]
    public void BuildSystemPrompt_WithEmptyContent_ReturnsBasePrompt()
    {
        var basePrompt = "You are a helpful assistant.";
        
        var result = AgentsMdLoader.BuildSystemPrompt(basePrompt, "   ");
        
        Assert.Equal(basePrompt, result);
    }

    [Fact]
    public async Task LoadAsync_WithExistingFile_ReturnsContent()
    {
        // Create a temp directory with AGENTS.md
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var agentsMdPath = Path.Combine(tempDir, AgentsMdLoader.FileName);
            var content = "# Test Project\n\nBuild: dotnet build";
            await File.WriteAllTextAsync(agentsMdPath, content);
            
            var result = await AgentsMdLoader.LoadAsync(tempDir);
            
            Assert.NotNull(result);
            Assert.Contains(content.Trim(), result);
            Assert.Contains("[.]", result); // Should have relative path marker
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithEmptyFile_IgnoresIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var agentsMdPath = Path.Combine(tempDir, AgentsMdLoader.FileName);
            await File.WriteAllTextAsync(agentsMdPath, "   \n   ");
            
            var result = await AgentsMdLoader.LoadAsync(tempDir);
            
            // Empty file should be ignored, and if there's no global config, result is null
            // (or contains only global if it exists)
            Assert.True(result == null || !result.Contains("[.]"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithNoFile_ReturnsNullOrGlobalOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var result = await AgentsMdLoader.LoadAsync(tempDir);
            
            // Should be null if no global config, or only global content
            Assert.True(result == null || !result.Contains("[.]"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
