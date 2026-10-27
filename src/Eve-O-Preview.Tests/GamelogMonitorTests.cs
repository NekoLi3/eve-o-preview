using System;
using System.IO;
using System.Text;
using EveOPreview.Configuration.Implementation;
using EveOPreview.Services.Implementation;
using Xunit;

namespace EveOPreview.Tests
{
    public class GamelogMonitorTests
    {
        private const string Header = "Listener:        Iryin\r\n\r\n";
        private const string MarkerPrefix = "\uFEFF[ 2026.10.27 01:35:18 ] EVE System > Channel changed to Local : ";

        [Fact]
        public void Reads_The_Last_System_From_A_Chatlog()
        {
            using var fixture = new Fixture();
            fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header +
                MarkerPrefix + "0MV-4W\r\n" +
                MarkerPrefix + "TVN-FM\r\n" +
                MarkerPrefix + "0MV-4W\r\n");

            fixture.Monitor.RefreshNow();

            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        [Fact]
        public void Reads_System_Markers_Appended_After_The_First_Refresh()
        {
            using var fixture = new Fixture();
            string path = fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header + MarkerPrefix + "0MV-4W\r\n");

            fixture.Monitor.RefreshNow();
            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));

            fixture.Append(path, MarkerPrefix + "TVN-FM\r\n");
            fixture.Monitor.RefreshNow();
            Assert.Equal("TVN-FM", fixture.Monitor.GetSystemForCharacter("Iryin"));

            fixture.Append(path, MarkerPrefix + "0MV-4W\r\n");
            fixture.Monitor.RefreshNow();
            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        [Fact]
        public void Same_File_Updates_Even_When_The_File_Timestamp_Is_Stale()
        {
            using var fixture = new Fixture();
            string path = fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header + MarkerPrefix + "0MV-4W\r\n");

            fixture.Monitor.RefreshNow();
            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));

            fixture.Append(path, MarkerPrefix + "TVN-FM\r\n");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));

            fixture.Monitor.RefreshNow();

            Assert.Equal("TVN-FM", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        [Fact]
        public void Waits_For_A_Complete_Line_Before_Updating_The_System()
        {
            using var fixture = new Fixture();
            string path = fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header + MarkerPrefix + "0MV-4W\r\n");

            fixture.Monitor.RefreshNow();
            fixture.Append(path, MarkerPrefix + "TVN-FM");
            fixture.Monitor.RefreshNow();

            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));

            fixture.Append(path, "\r\n");
            fixture.Monitor.RefreshNow();

            Assert.Equal("TVN-FM", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        [Fact]
        public void Uses_The_Newest_File_When_A_Chatlog_Is_Rotated()
        {
            using var fixture = new Fixture();
            string oldPath = fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header + MarkerPrefix + "TVN-FM\r\n");

            fixture.Monitor.RefreshNow();
            Assert.Equal("TVN-FM", fixture.Monitor.GetSystemForCharacter("Iryin"));

            File.Delete(oldPath);
            fixture.CreateChatlog(
                "Local_20261026_212259_2124762271.txt",
                Header + MarkerPrefix + "0MV-4W\r\n");

            fixture.Monitor.RefreshNow();

            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        [Fact]
        public void Newest_File_Wins_When_The_Same_Character_Has_Multiple_Logs()
        {
            using var fixture = new Fixture();
            string oldPath = fixture.CreateChatlog(
                "Local_20261026_211905_2124762271.txt",
                Header + MarkerPrefix + "TVN-FM\r\n");
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddMinutes(-10));

            fixture.Monitor.RefreshNow();
            Assert.Equal("TVN-FM", fixture.Monitor.GetSystemForCharacter("Iryin"));

            string newPath = fixture.CreateChatlog(
                "Local_20261026_212259_2124762271.txt",
                Header + MarkerPrefix + "0MV-4W\r\n");
            File.SetLastWriteTimeUtc(newPath, DateTime.UtcNow);

            fixture.Monitor.RefreshNow();

            Assert.Equal("0MV-4W", fixture.Monitor.GetSystemForCharacter("Iryin"));
        }

        private sealed class Fixture : IDisposable
        {
            public string DirectoryPath { get; }
            public ThumbnailConfiguration Configuration { get; }
            public GamelogMonitor Monitor { get; }

            public Fixture()
            {
                this.DirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "eveopreview-gamelog-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(this.DirectoryPath);

                this.Configuration = new ThumbnailConfiguration
                {
                    ChatlogsPath = this.DirectoryPath
                };
                this.Monitor = new GamelogMonitor(this.Configuration);
            }

            public string CreateChatlog(string fileName, string content)
            {
                string path = Path.Combine(this.DirectoryPath, fileName);
                File.WriteAllText(path, content, Encoding.Unicode);
                return path;
            }

            public void Append(string path, string content)
            {
                File.AppendAllText(path, content, Encoding.Unicode);
            }

            public void Dispose()
            {
                this.Monitor.Stop();
                Directory.Delete(this.DirectoryPath, true);
            }
        }
    }
}
