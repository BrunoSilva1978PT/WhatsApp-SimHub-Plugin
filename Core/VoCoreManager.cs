using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SimHub.Plugins;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHub.Plugins.Devices;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Manages VoCore devices configuration (zero reflection, direct property access)
    /// Simplified: User controls dashboard via UI, backend only ensures overlay is ON and applies correct dashboard
    /// Device discovery delegated to DeviceDiscoveryManager.
    /// </summary>
    public class VoCoreManager
    {
        private readonly PluginManager _pluginManager;
        private readonly Action<string> _log;
        private readonly DashboardMerger _dashboardMerger;
        private readonly DeviceDiscoveryManager _discovery;
        private volatile bool _isMerging;

        public VoCoreManager(PluginManager pluginManager, DashboardMerger dashboardMerger, DeviceDiscoveryManager discovery, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _dashboardMerger = dashboardMerger;
            _discovery = discovery;
            _log = log;
        }

        /// <summary>
        /// Check if a dashboard exists in SimHub (uses official API, zero I/O, zero reflection)
        /// </summary>
        public bool DoesDashboardExist(string dashboardName)
        {
            if (string.IsNullOrEmpty(dashboardName))
                return false;

            try
            {
                var metadata = _pluginManager?.GetDashboardMetadata(dashboardName);
                return metadata != null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DashboardCheck] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get all connected VoCore devices with their current state.
        /// Delegates to DeviceDiscoveryManager.
        /// </summary>
        public List<VoCoreDevice> GetConnectedDevices()
        {
            return _discovery.GetVoCoreDevices();
        }

        /// <summary>
        /// Ensure VoCore has Information Overlay ON and correct dashboard
        /// Called periodically from DataUpdate()
        /// </summary>
        public void EnsureOverlayEnabled(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber) || _isMerging)
                return;

            try
            {
                VOCORESettings vocoreSettings = _discovery.FindVoCoreBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                // Only ensure Information Overlay is ON - don't touch the dashboard
                if (!vocoreSettings.UseOverlayDashboard)
                {
                    vocoreSettings.UseOverlayDashboard = true;
                    _log?.Invoke($"[EnsureOverlay] Information Overlay enabled for '{serialNumber}'");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"EnsureOverlayEnabled error: {ex.Message}");
            }
        }

        /// <summary>
        /// Set a dashboard on a VoCore and enable overlay
        /// </summary>
        public void SetDashboard(string serialNumber, int vocoreNumber, string dashboardName)
        {
            if (string.IsNullOrEmpty(serialNumber) || string.IsNullOrEmpty(dashboardName))
                return;

            try
            {
                VOCORESettings vocoreSettings = _discovery.FindVoCoreBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                vocoreSettings.UseOverlayDashboard = true;
                vocoreSettings.CurrentOverlayDashboard.Dashboard = dashboardName;
                _log?.Invoke($"[SetDashboard] VoCore {vocoreNumber}: Dashboard set to '{dashboardName}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"SetDashboard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply merged dashboard (2 layers mode)
        /// Creates merge of Layer1 (base) + Layer2 (overlay on top)
        /// </summary>
        public void ApplyMerged(string serialNumber, int vocoreNumber, string layer1Dashboard, string layer2Dashboard)
        {
            if (string.IsNullOrEmpty(serialNumber) ||
                string.IsNullOrEmpty(layer1Dashboard) ||
                string.IsNullOrEmpty(layer2Dashboard))
                return;

            _isMerging = true;
            try
            {
                string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                string mergedDashboardPath = Path.Combine(_dashboardMerger.DashTemplatesPath, mergedDashboard);

                VOCORESettings vocoreSettings = _discovery.FindVoCoreBySerial(serialNumber);
                if (vocoreSettings == null)
                {
                    _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Device not found!");
                    return;
                }

                // STEP 1: Turn OFF Information Overlay (force SimHub to release cache)
                vocoreSettings.UseOverlayDashboard = false;
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Overlay disabled (forcing cache clear)");

                // STEP 2: Delete old merged dashboard (now that cache is cleared)
                if (Directory.Exists(mergedDashboardPath))
                {
                    try
                    {
                        Directory.Delete(mergedDashboardPath, true);
                        _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Deleted old merged dashboard");
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[ApplyMerged] Warning: Could not delete old merged dashboard: {ex.Message}");
                    }
                }

                // STEP 3: Do merge (creates new .djson)
                _dashboardMerger.MergeDashboards(layer1Dashboard, layer2Dashboard, vocoreNumber);
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Merge completed ('{layer1Dashboard}' + '{layer2Dashboard}')");

                // STEP 4: Turn overlay back ON with the merged dashboard
                vocoreSettings.UseOverlayDashboard = true;
                vocoreSettings.CurrentOverlayDashboard.Dashboard = mergedDashboard;
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Overlay enabled with '{mergedDashboard}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ApplyMerged error: {ex.Message}");
            }
            finally
            {
                _isMerging = false;
            }
        }

        /// <summary>
        /// Check if merged dashboard exists for a specific VoCore
        /// </summary>
        public bool MergedDashboardExists(int vocoreNumber)
        {
            string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
            return DoesDashboardExist(mergedDashboard);
        }

        /// <summary>
        /// Delete merged dashboard for a specific VoCore
        /// </summary>
        public void DeleteMergedDashboard(int vocoreNumber)
        {
            try
            {
                string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                string mergedPath = Path.Combine(_dashboardMerger.DashTemplatesPath, mergedDashboard);

                if (Directory.Exists(mergedPath))
                {
                    Directory.Delete(mergedPath, true);
                    _log?.Invoke($"[DeleteMerged] Deleted: {mergedDashboard}");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"DeleteMergedDashboard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the Information Overlay dashboard (set to empty)
        /// Called when user deselects a VoCore from a slot
        /// Only clears the dashboard - does not disable overlay or change any other settings
        /// </summary>
        public void ClearOverlayDashboard(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                _log?.Invoke($"[ClearOverlay] Serial number is empty, skipping");
                return;
            }

            try
            {
                VOCORESettings vocoreSettings = _discovery.FindVoCoreBySerial(serialNumber);
                if (vocoreSettings == null)
                {
                    _log?.Invoke($"[ClearOverlay] Device not found for '{serialNumber}'");
                    return;
                }

                // Disable overlay and clear dashboard directly
                vocoreSettings.UseOverlayDashboard = false;
                vocoreSettings.CurrentOverlayDashboard.Dashboard = null;
                _log?.Invoke($"[ClearOverlay] Overlay disabled and dashboard set to null for '{serialNumber}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ClearOverlayDashboard error: {ex.Message}");
            }
        }
    }
}
