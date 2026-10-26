using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EveOPreview.Services.Implementation
{
	/// <summary>
	/// Reads only the newly appended part of a UTF-8 log file. The caller owns the
	/// byte offset, so an incomplete trailing line is naturally retried on the next
	/// pass instead of being parsed twice.
	/// </summary>
	internal static class LogTailReader
	{
		internal const int BootstrapBackfillBytes = 64 * 1024;
		internal const int MaxReadBytes = 256 * 1024;

		internal static List<string> ReadLines(FileStream stream, ref long offset)
		{
			var lines = new List<string>();
			long length = stream.Length;
			if (offset < 0 || offset > length)
			{
				offset = 0;
			}

			bool skipPartialFirstLine = false;
			if (offset == 0 && length > BootstrapBackfillBytes)
			{
				// A fresh file can be huge (a long play session). Only the recent
				// tail matters for temporary labels.
				offset = length - BootstrapBackfillBytes;
				skipPartialFirstLine = true;
			}

			long available = length - offset;
			if (available <= 0)
			{
				return lines;
			}

			if (available > MaxReadBytes)
			{
				// A very large burst of writes (or a stalled watcher): skip ahead
				// rather than reading megabytes of stale lines.
				offset = length - MaxReadBytes;
				available = MaxReadBytes;
				skipPartialFirstLine = true;
			}

			int bufferSize = (int)available;
			byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
			try
			{
				stream.Seek(offset, SeekOrigin.Begin);
				int read = 0;
				while (read < bufferSize)
				{
					int chunk = stream.Read(buffer, read, bufferSize - read);
					if (chunk <= 0)
					{
						break;
					}
					read += chunk;
				}

				if (read == 0)
				{
					return lines;
				}

				int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1, read);
				if (lastNewline < 0)
				{
					// No complete line yet. Keep the offset and retry later.
					return lines;
				}

				string text = Encoding.UTF8.GetString(buffer, 0, lastNewline + 1);
				offset += lastNewline + 1;

				string[] rawLines = text.Split('\n');
				int start = 0;
				if (skipPartialFirstLine && rawLines.Length > 0)
				{
					// The first line is a fragment of a line that started before the
					// backfill window.
					start = 1;
				}

				for (int i = start; i < rawLines.Length; i++)
				{
					string line = rawLines[i].TrimEnd('\r');
					if (line.Length > 0)
					{
						lines.Add(line);
					}
				}

				return lines;
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}
