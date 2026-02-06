using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Manages sound notifications for VIP/Urgent messages.
    /// Uses WPF MediaPlayer (supports .wav and .mp3).
    /// </summary>
    public class SoundManager : IDisposable
    {
        private readonly string _soundsFolder;
        private readonly Action<string> _log;
        private MediaPlayer _player;
        private static readonly string[] SupportedExtensions = { ".wav", ".mp3" };

        public SoundManager(string pluginFolder, Action<string> log = null)
        {
            _log = log;
            _soundsFolder = Path.Combine(pluginFolder, "sounds");
        }

        /// <summary>
        /// Extracts default sounds from embedded resources on first run.
        /// Only creates folder and extracts if folder doesn't exist.
        /// </summary>
        public void ExtractDefaultSounds()
        {
            try
            {
                if (Directory.Exists(_soundsFolder))
                {
                    _log?.Invoke("[Sound] Sounds folder already exists - skipping extraction");
                    return;
                }

                Directory.CreateDirectory(_soundsFolder);
                _log?.Invoke($"[Sound] Created sounds folder: {_soundsFolder}");

                var assembly = Assembly.GetExecutingAssembly();
                var prefix = "WhatsAppSimHubPlugin.Resources.sounds.";

                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(prefix))
                    .ToList();

                foreach (var resourceName in resourceNames)
                {
                    var fileName = resourceName.Substring(prefix.Length);
                    var targetPath = Path.Combine(_soundsFolder, fileName);

                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) continue;

                        using (var fileStream = File.Create(targetPath))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                }

                _log?.Invoke($"[Sound] Extracted {resourceNames.Count} default sounds");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sound] ExtractDefaultSounds error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns list of available sound file names in the sounds folder.
        /// </summary>
        public List<string> GetAvailableSounds()
        {
            try
            {
                if (!Directory.Exists(_soundsFolder))
                    return new List<string>();

                return Directory.GetFiles(_soundsFolder)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .Select(Path.GetFileName)
                    .OrderBy(f => f)
                    .ToList();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sound] GetAvailableSounds error: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Plays a sound file from the sounds folder.
        /// Must be called from UI thread (WPF MediaPlayer requirement).
        /// </summary>
        public void PlaySound(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    return;

                var filePath = Path.Combine(_soundsFolder, fileName);
                if (!File.Exists(filePath))
                {
                    _log?.Invoke($"[Sound] File not found: {fileName}");
                    return;
                }

                if (_player == null)
                    _player = new MediaPlayer();

                _player.Open(new Uri(filePath));
                _player.Play();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sound] PlaySound error: {ex.Message}");
            }
        }

        /// <summary>
        /// Imports a sound file (copies to sounds folder).
        /// Returns the file name if successful, null otherwise.
        /// </summary>
        public string ImportSound(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return null;

                var ext = Path.GetExtension(sourcePath).ToLower();
                if (!SupportedExtensions.Contains(ext))
                {
                    _log?.Invoke($"[Sound] Unsupported format: {ext}");
                    return null;
                }

                if (!Directory.Exists(_soundsFolder))
                    Directory.CreateDirectory(_soundsFolder);

                var fileName = Path.GetFileName(sourcePath);
                var targetPath = Path.Combine(_soundsFolder, fileName);

                // If file with same name exists, add number suffix
                int counter = 1;
                while (File.Exists(targetPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                    fileName = $"{nameWithoutExt}_{counter}{ext}";
                    targetPath = Path.Combine(_soundsFolder, fileName);
                    counter++;
                }

                File.Copy(sourcePath, targetPath);
                _log?.Invoke($"[Sound] Imported: {fileName}");
                return fileName;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sound] ImportSound error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes a sound file from the sounds folder.
        /// </summary>
        public bool DeleteSound(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    return false;

                var filePath = Path.Combine(_soundsFolder, fileName);
                if (!File.Exists(filePath))
                    return false;

                File.Delete(filePath);
                _log?.Invoke($"[Sound] Deleted: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Sound] DeleteSound error: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            _player?.Close();
        }
    }
}
