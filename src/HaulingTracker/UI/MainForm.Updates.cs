using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HaulingTracker.UI;

public sealed partial class MainForm
{
    readonly ToolStripMenuItem _versionItem = new() { Enabled = false };
    readonly ToolStripMenuItem _updateBtn = new();
    bool _updateBusy;

    void InitUpdater()
    {
        var own = Updater.OwnCommit;
        _versionItem.Text = own == null ? "Version: built on this computer" : "Version: " + own[..7];
        _updateBtn.Text = "Check for Updates…";
        _updateBtn.Click += async (_, _) => await CheckForUpdateAsync(true);
        Shown += async (_, _) => await CheckForUpdateAsync(false);
    }

    /// <summary>
    /// Asks GitHub for the newest build. At startup it says nothing unless there is something newer (and then asks first);
    /// from the menu it always answers.
    /// </summary>
    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        var asked = false;
        try
        {
            var own = Updater.OwnCommit;
            if (own == null)
            {
                if (manual) MessageBox.Show(this, "This program was built on your own computer, so it does not update itself.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var latest = await Updater.LatestCommitAsync(cts.Token);
            if (latest == null)
            {
                if (manual) MessageBox.Show(this, "Could not read the newest version from GitHub.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!await Updater.IsNewerAsync(own, latest, cts.Token))
            {
                if (manual) MessageBox.Show(this, "You have the newest version.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            asked = true;
            var answer = MessageBox.Show(this,
                "A newer version of the Hauling Session Tracker is available.\n\nUpdate now? The program restarts. Your data is not touched, and the old program is kept as HaulingTracker.old.exe.",
                "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            SetStatus("Downloading the update…");
            var exe = await Updater.DownloadAsync();
            SetStatus("Installing the update…");
            Updater.ReplaceAndStart(exe);
            Application.Exit();
        }
        catch (Exception ex)
        {
            SetStatus("The update did not work: " + ex.Message);
            if (manual || asked) MessageBox.Show(this, "The update did not work: " + ex.Message, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateBusy = false;
        }
    }
}
