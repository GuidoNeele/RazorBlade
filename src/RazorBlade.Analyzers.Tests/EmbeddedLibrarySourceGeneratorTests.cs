﻿using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using RazorBlade.Analyzers.Tests.Support;
using RazorBlade.Tests.Support;

namespace RazorBlade.Analyzers.Tests;

[TestFixture]
public class EmbeddedLibrarySourceGeneratorTests
{
    [Test]
    [TestCase(LanguageVersion.CSharp10)]
    [TestCase(LanguageVersion.CSharp11)]
    [TestCase(EmbeddedLibrarySourceGenerator.MinimumSupportedLanguageVersion)]
    public void should_generate_valid_source(LanguageVersion languageVersion)
    {
        // C# 7.3 is the latest version officially supported for the netstandard2.0 target,
        // but it's really old at this point. We'll ask the user to upgrade to a newer version.

        var (generatorDiagnostics, compilation) = RunGenerator(languageVersion);

        generatorDiagnostics.ShouldBeEmpty();
        compilation.GetDiagnostics().Where(i => i.Severity >= DiagnosticSeverity.Warning).ShouldBeEmpty();

        compilation.Assembly.GetTypeByMetadataName(typeof(HtmlTemplate<>).FullName!).ShouldNotBeNull();

        foreach (var exportedType in typeof(HtmlTemplate).Assembly.GetExportedTypes())
            compilation.Assembly.GetTypeByMetadataName(exportedType.FullName!).ShouldNotBeNull();
    }

    [Test]
    public void should_generate_diagnostic_on_unsupported_language_version()
    {
        var (generatorDiagnostics, _) = RunGenerator(LanguageVersion.CSharp9);

        generatorDiagnostics.ShouldContain(Diagnostics.EmbeddedLibraryUnsupportedCSharpVersion(EmbeddedLibrarySourceGenerator.MinimumSupportedLanguageVersion));
    }

    private static (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation compilation) RunGenerator(LanguageVersion languageVersion)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(languageVersion);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var compilation = CSharpCompilation.Create("TestAssembly")
                                           .AddReferences(
                                               MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                                               MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
                                               MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll"))
                                           )
                                           .AddSyntaxTrees(CSharpSyntaxTree.ParseText(string.Empty, parseOptions))
                                           .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

        var runResult = CSharpGeneratorDriver.Create([new EmbeddedLibrarySourceGenerator().AsSourceGenerator()], parseOptions: parseOptions)
                                             .WithUpdatedAnalyzerConfigOptions(new AnalyzerConfigOptionsProviderMock
                                             {
                                                 { Constants.GlobalOptions.EmbeddedLibrary, bool.TrueString }
                                             })
                                             .RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _)
                                             .GetRunResult();

        return (runResult.Diagnostics, updatedCompilation);
    }
}
