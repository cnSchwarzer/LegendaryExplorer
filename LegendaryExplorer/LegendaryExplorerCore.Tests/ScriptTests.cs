﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.UnrealScript;
using LegendaryExplorerCore.UnrealScript.Compiling.Errors;
using LegendaryExplorerCore.UnrealScript.Language.Tree;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorerCore.Tests
{
    [TestClass]
    public class ScriptTests
    {
        [TestMethod]
        public void TestScript()
        {
            GlobalTest.Init();

            string testDataDirectory = Path.Combine(GlobalTest.GetTestPackagesDirectory(), "PC");
            var testFiles = Directory.GetFiles(testDataDirectory, "*.*", SearchOption.AllDirectories)
                                     .Where(x => x.RepresentsPackageFilePath() && !x.Contains("UDK", StringComparison.InvariantCultureIgnoreCase)).ToList();

            //var testFile = Path.Combine(GlobalTest.GetTestPackagesDirectory(), "PC", "ME1", "BIOA_NOR10_08_DSG.SFM");
            //var testFile = Path.Combine(GlobalTest.GetTestPackagesDirectory(), "PC", "ME2", "retail", "BioD_BlbGtl_205Evacuation.pcc");
            //var testFile = Path.Combine(GlobalTest.GetTestPackagesDirectory(), "PC", "ME3", "BioP_ProEar.pcc");

            foreach (var testFile in testFiles)
            {
                var shortName = Path.GetRelativePath(testDataDirectory, testFile);
                compileTest(testFile, shortName, true);
            }
            FileLib.FreeLibs();
            MemoryAnalyzer.ForceFullGC(true);
            foreach (var testFile in testFiles)
            {
                var shortName = Path.GetRelativePath(testDataDirectory, testFile);
                compileTest(testFile, shortName, false);
            }
        }

        private static void compileTest(string testFile, string shortName, bool usePackageCache)
        {
            MEPackageHandler.GlobalSharedCacheEnabled = !usePackageCache;

            using var testPackage = MEPackageHandler.OpenMEPackage(testFile);
            if (testPackage.Platform != MEPackage.GamePlatform.PC)
                return; // Skip this file.

            Stopwatch sw = Stopwatch.StartNew();
            var testLib = new FileLib(testPackage);
            bool fileLibInitialized = testLib.InitializeAsync(usePackageCache ? new PackageCache() : null).Result;
            Assert.IsTrue(fileLibInitialized, $"{testPackage.Game} Script failed to compile {shortName} class definitions! Errors:\n{string.Join('\n', testLib.InitializationLog.Content)}");
            sw.Stop();
            Debug.WriteLine($"With {(usePackageCache ? "packagecache" : "globalcache")} took {sw.ElapsedMilliseconds}ms to initialize lib");

            foreach (ExportEntry funcExport in testPackage.Exports.Where(exp => exp.ClassName == "Function"))
            {
                (ASTNode astNode, string text) = UnrealScriptCompiler.DecompileExport(funcExport, testLib);

                Assert.IsInstanceOfType(astNode, typeof(Function), $"#{funcExport.UIndex} {funcExport.InstancedFullPath} in {shortName} did not decompile!");

                /* SirCxyrtyx: Disabling recompilation tests because succesfull re-compilation of all functions will never happen
                 * For re-compilation testing to be useful, it will need to be targeted
                 */
                //(_, MessageLog log) = UnrealScriptCompiler.CompileFunction(funcExport, text, testLib);

                //if (Enumerable.Any(log.AllErrors))
                //{
                //    Assert.Fail($"#{funcExport.UIndex} {funcExport.InstancedFullPath} in {shortName} did not recompile!");
                //}
            }
        }
    }
}
