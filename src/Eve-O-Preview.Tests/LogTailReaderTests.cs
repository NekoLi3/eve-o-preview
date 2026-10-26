using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EveOPreview.Services.Implementation;
using Xunit;

namespace EveOPreview.Tests
{
    public class LogTailReaderTests
    {
        [Fact]
        public void Reads_Only_Appended_Lines()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                Append(stream, "line1\n");

                long offset = 0;
                List<string> first = LogTailReader.ReadLines(stream, ref offset);

                Assert.Equal(new[] { "line1" }, first);
                Assert.Equal(Encoding.UTF8.GetByteCount("line1\n"), offset);

                Append(stream, "line2\n");
                List<string> second = LogTailReader.ReadLines(stream, ref offset);

                Assert.Equal(new[] { "line2" }, second);
                Assert.Equal(Encoding.UTF8.GetByteCount("line1\nline2\n"), offset);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Keeps_Incomplete_Trailing_Line_For_The_Next_Read()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                Append(stream, "line1\npartial");

                long offset = 0;
                Assert.Equal(new[] { "line1" }, LogTailReader.ReadLines(stream, ref offset));
                Assert.Equal(Encoding.UTF8.GetByteCount("line1\n"), offset);

                Append(stream, " done\n");
                Assert.Equal(new[] { "partial done" }, LogTailReader.ReadLines(stream, ref offset));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Retries_When_No_Complete_Line_Is_Available()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                Append(stream, "no newline yet");

                long offset = 0;
                Assert.Empty(LogTailReader.ReadLines(stream, ref offset));
                Assert.Equal(0, offset);

                Append(stream, "\n");
                Assert.Equal(new[] { "no newline yet" }, LogTailReader.ReadLines(stream, ref offset));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Decodes_Multibyte_Character_Split_Across_Writes()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                byte[] bytes = Encoding.UTF8.GetBytes("caf\u00e9\n");

                // Split inside the two-byte UTF-8 sequence for the accented character.
                stream.Write(bytes, 0, 4);
                stream.Flush();

                long offset = 0;
                Assert.Empty(LogTailReader.ReadLines(stream, ref offset));
                Assert.Equal(0, offset);

                stream.Write(bytes, 4, bytes.Length - 4);
                stream.Flush();

                Assert.Equal(new[] { "caf\u00e9" }, LogTailReader.ReadLines(stream, ref offset));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Resets_Offset_When_File_Is_Truncated()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                Append(stream, "first line\n");

                long offset = 0;
                Assert.Equal(new[] { "first line" }, LogTailReader.ReadLines(stream, ref offset));
                Assert.True(offset > 2);

                stream.SetLength(0);
                stream.Seek(0, SeekOrigin.Begin);
                Append(stream, "x\n");

                Assert.Equal(new[] { "x" }, LogTailReader.ReadLines(stream, ref offset));
                Assert.Equal(2, offset);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Bootstrap_Reads_Only_The_Recent_Tail_Of_A_Large_File()
        {
            string path = CreateTempFile();
            try
            {
                var builder = new StringBuilder();
                for (int i = 0; i < 8000; i++)
                {
                    builder.Append("line-").Append(i.ToString("D5")).Append('\n');
                }

                byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
                Assert.True(bytes.Length > LogTailReader.BootstrapBackfillBytes);

                using FileStream stream = Open(path);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                long offset = 0;
                List<string> lines = LogTailReader.ReadLines(stream, ref offset);

                Assert.NotEmpty(lines);
                Assert.All(lines, line => Assert.Matches("^line-[0-9]{5}$", line));
                Assert.InRange(offset, bytes.Length - LogTailReader.BootstrapBackfillBytes + 1, bytes.Length);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Large_Append_Skips_To_The_Recent_Tail()
        {
            string path = CreateTempFile();
            try
            {
                using FileStream stream = Open(path);
                Append(stream, "start\n");

                long offset = 0;
                Assert.Equal(new[] { "start" }, LogTailReader.ReadLines(stream, ref offset));

                var builder = new StringBuilder();
                for (int i = 0; i < 2000; i++)
                {
                    builder.Append(new string('a', 200)).Append('\n');
                }

                Append(stream, builder.ToString());
                Assert.True(stream.Length > offset + LogTailReader.MaxReadBytes);

                List<string> lines = LogTailReader.ReadLines(stream, ref offset);

                Assert.NotEmpty(lines);
                Assert.InRange(offset, stream.Length - LogTailReader.MaxReadBytes, stream.Length);
                Assert.All(lines.Skip(1), line => Assert.Equal(200, line.Length));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateTempFile()
        {
            return Path.Combine(Path.GetTempPath(), "eveopreview-" + Guid.NewGuid().ToString("N") + ".txt");
        }

        private static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }

        private static void Append(FileStream stream, string text)
        {
            stream.Seek(0, SeekOrigin.End);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }
}
