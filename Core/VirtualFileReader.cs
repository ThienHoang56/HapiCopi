using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace HapiCopi
{
    /// <summary>
    /// Một "file ảo" trên Clipboard: dữ liệu nằm trong bộ nhớ của tiến trình nguồn chứ không có đường dẫn trên đĩa.
    /// Xuất hiện khi copy tệp đính kèm trong Outlook, kéo ảnh từ trình duyệt, copy asset từ web app...
    /// </summary>
    public sealed class VirtualFile
    {
        public string FileName { get; init; } = "";
        public byte[] Content { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Đọc cặp định dạng FileGroupDescriptorW + FileContents từ Clipboard.
    ///
    /// WinForms IDataObject không đọc được FileContents khi có nhiều hơn một tệp, vì nó không truyền
    /// được chỉ số lindex xuống nguồn. Phải hạ xuống COM IDataObject để chỉ định FORMATETC.lindex.
    /// </summary>
    public static class VirtualFileReader
    {
        public const string FileGroupDescriptorW = "FileGroupDescriptorW";
        public const string FileGroupDescriptor = "FileGroupDescriptor";
        public const string FileContents = "FileContents";

        private const int MaxFilesScanned = 32;

        // FILEDESCRIPTORW: dwFlags(4) + clsid(16) + sizel(8) + pointl(8) + dwFileAttributes(4)
        //                + 3 FILETIME(24) + nFileSizeHigh(4) + nFileSizeLow(4) + cFileName(260 WCHAR = 520)
        private const int FileDescriptorSize = 592;
        private const int FileNameOffset = 72;
        private const int MaxPathChars = 260;

        [Flags]
        private enum Tymed
        {
            HGlobal = 1,
            IStream = 4,
            IStorage = 8
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        /// <summary>
        /// Liệt kê tên các file ảo trên clipboard. Không đọc nội dung nên rẻ, dùng để lọc trước.
        /// </summary>
        public static List<string> GetVirtualFileNames(System.Windows.Forms.IDataObject dataObject)
        {
            var names = new List<string>();
            try
            {
                bool wide = dataObject.GetDataPresent(FileGroupDescriptorW);
                if (!wide && !dataObject.GetDataPresent(FileGroupDescriptor))
                {
                    return names;
                }

                object? raw = dataObject.GetData(wide ? FileGroupDescriptorW : FileGroupDescriptor);
                byte[]? buffer = raw switch
                {
                    MemoryStream ms => ms.ToArray(),
                    byte[] bytes => bytes,
                    _ => null
                };

                if (buffer == null || buffer.Length < 4)
                {
                    return names;
                }

                int count = BitConverter.ToInt32(buffer, 0);
                if (count <= 0)
                {
                    return names;
                }

                count = Math.Min(count, MaxFilesScanned);

                for (int i = 0; i < count; i++)
                {
                    int descriptorStart = 4 + (i * FileDescriptorSize);
                    if (descriptorStart + FileDescriptorSize > buffer.Length)
                    {
                        break;
                    }

                    string name = wide
                        ? Encoding.Unicode.GetString(buffer, descriptorStart + FileNameOffset, MaxPathChars * 2)
                        : Encoding.Default.GetString(buffer, descriptorStart + FileNameOffset, MaxPathChars);

                    int terminator = name.IndexOf('\0');
                    if (terminator >= 0)
                    {
                        name = name.Substring(0, terminator);
                    }

                    names.Add(name);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[VirtualFile] Không đọc được FileGroupDescriptor: {ex.Message}");
            }

            return names;
        }

        /// <summary>
        /// Đọc nội dung file ảo thứ <paramref name="index"/> (theo thứ tự trong FileGroupDescriptor).
        /// Trả về null nếu nguồn không cấp được dữ liệu.
        /// </summary>
        public static byte[]? ReadContent(System.Windows.Forms.IDataObject dataObject, int index, long maxBytes)
        {
            if (dataObject is not ComTypes.IDataObject comData)
            {
                Program.Log("[VirtualFile] Clipboard không hỗ trợ COM IDataObject, bỏ qua file ảo.");
                return null;
            }

            short contentsFormatId = (short)System.Windows.Forms.DataFormats.GetFormat(FileContents).Id;

            var formatEtc = new ComTypes.FORMATETC
            {
                cfFormat = contentsFormatId,
                dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = index,
                ptd = IntPtr.Zero,
                tymed = (ComTypes.TYMED)(Tymed.IStream | Tymed.IStorage | Tymed.HGlobal)
            };

            ComTypes.STGMEDIUM medium = default;
            try
            {
                comData.GetData(ref formatEtc, out medium);

                return medium.tymed switch
                {
                    ComTypes.TYMED.TYMED_ISTREAM => ReadFromStream(medium.unionmember, maxBytes),
                    ComTypes.TYMED.TYMED_HGLOBAL => ReadFromHGlobal(medium.unionmember, maxBytes),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Program.Log($"[VirtualFile] Lỗi đọc FileContents[{index}]: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    ComTypes.STGMEDIUM toRelease = medium;
                    ReleaseStgMedium(ref toRelease);
                }
                catch { }
            }
        }

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);

        private static byte[]? ReadFromStream(IntPtr streamPointer, long maxBytes)
        {
            if (streamPointer == IntPtr.Zero)
            {
                return null;
            }

            object? unknown = Marshal.GetObjectForIUnknown(streamPointer);
            if (unknown is not ComTypes.IStream stream)
            {
                return null;
            }

            stream.Stat(out ComTypes.STATSTG stat, 1 /* STATFLAG_NONAME */);
            if (stat.cbSize <= 0 || stat.cbSize > maxBytes)
            {
                Program.Log($"[VirtualFile] Bỏ qua stream kích thước {stat.cbSize} bytes (giới hạn {maxBytes}).");
                return null;
            }

            var buffer = new byte[stat.cbSize];
            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                stream.Read(buffer, buffer.Length, bytesReadPtr);
                int bytesRead = Marshal.ReadInt32(bytesReadPtr);
                if (bytesRead <= 0)
                {
                    return null;
                }
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                return buffer;
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }
        }

        private static byte[]? ReadFromHGlobal(IntPtr hGlobal, long maxBytes)
        {
            if (hGlobal == IntPtr.Zero)
            {
                return null;
            }

            IntPtr pointer = GlobalLock(hGlobal);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                long size = (long)GlobalSize(hGlobal);
                if (size <= 0 || size > maxBytes)
                {
                    return null;
                }

                var buffer = new byte[size];
                Marshal.Copy(pointer, buffer, 0, (int)size);
                return buffer;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }
    }
}
