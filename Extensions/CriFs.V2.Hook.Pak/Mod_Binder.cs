using System.Diagnostics;
using System.Runtime.InteropServices;
using CriFs.V2.Hook.Interfaces;
using CriFs.V2.Hook.Interfaces.Structs;
using FileEmulationFramework.Lib;

namespace CriFs.V2.Hook.Pak;

/// <summary>
/// Contains the binding logic.
/// Not separated from mod class as this mod's small.
/// </summary>
public partial class Mod
{
    // Mod implementation.
    private const string R2 = "R2"; // DO NOT CHANGE
    private readonly List<string> _boundFiles = new();
    
    private void OnCpkUnbind(ICriFsRedirectorApi.UnbindContext bind)
    {
        foreach (var boundFile in CollectionsMarshal.AsSpan(_boundFiles))
            _pakEmulator.InvalidateFile(boundFile);

        _boundFiles.Clear();
    }

    private void OnCpkBind(ICriFsRedirectorApi.BindContext bind)
    {
        // Note: After profiling, no caching needed here.
        var input    = _pakEmulator.GetEmulatorInput();
        var cpks     = _criFsApi.GetCpkFilesInGameDir();
        var criFsLib = _criFsApi.GetCriFsLib();
        var tasks = new List<Task>();
        var watch = Stopwatch.StartNew();
        
        foreach (var inputItem in input)
        {
            var route = new Route(inputItem.Route);

            if (!TryFindPakInAnyCpk(route, cpks, out var cpkPath, out var cachedFile, out int pakFileIndex))
            {
                _logger.Error("[CriFsV2.Pak] PAK file for {0} not found in any CPK!!", route.FullPath);
                continue;
            }
            
            // Get matched file.
            var pakFile = cachedFile.Files[pakFileIndex];
            _logger.Info("[CriFsV2.Pak] Found PAK file {0} in CPK {1}", route.FullPath, cpkPath);

            
            // Register PAK
            var pakBindPath = Path.Combine(R2, pakFile.FullPath);
            if (bind.RelativePathToFileMap.ContainsKey(pakBindPath))
            {
                _logger.Info("[CriFsV2.Pak] Binder input already contains PAK {0}, we'll use existing one.", pakFile.FullPath);
            }
            else
            {
                var emulatedFilePath = Path.Combine(bind.BindDirectory, pakBindPath);
                Directory.CreateDirectory(Path.GetDirectoryName(emulatedFilePath)!);
                _logger.Info("[CriFsV2.Pak] Creating Emulated File {0}", emulatedFilePath);
                _pakEmulator.TryCreateFromFileSlice(cpkPath, pakFile.File.FileOffset, route.FullPath, emulatedFilePath);
                _boundFiles.Add(emulatedFilePath);
            }
            
        }

        Task.WhenAll(tasks).Wait();
        _logger.Debug($"[CriFsV2.Pak] Setup PAK Redirector Support for CRIFsHook in {watch.ElapsedMilliseconds}ms");
    }

    private bool TryFindPakInAnyCpk(Route route, string[] cpkFiles, out string cpkPath, out CpkCacheEntry cachedFile, out int fileIndex)
    {
        foreach (var cpk in cpkFiles)
        {
            cpkPath = cpk;
            cachedFile = _criFsApi.GetCpkFilesCached(cpk);
            var fileNameSpan = Path.GetFileName(route.FullPath.AsSpan());
                
            // If we find, check for ACB.
            if (cachedFile.FilesByPath.TryGetValue(route.FullPath, out fileIndex))
                return true;

            if (!cachedFile.FilesByFileName.TryGetValue(fileNameSpan.ToString(), out fileIndex)) 
                continue;
            
            // If route only has file name, we can take this as answer.
            if (Path.GetDirectoryName(route.FullPath) == null)
                return true;
            
            // If matches by file name we have to search all routes because it's possible duplicate
            // file names can exist under different subfolders
            for (var x = 0; x < cachedFile.Files.Length; x++)
            {
                var file = cachedFile.Files[x];
                if (!new Route(file.FullPath).Matches(route.FullPath)) 
                    continue;
                
                fileIndex = x;
                return true;
            }
        }

        cpkPath = string.Empty;
        fileIndex = -1;
        cachedFile = default;
        return false;
    }
}