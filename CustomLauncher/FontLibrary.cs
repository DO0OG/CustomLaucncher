using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomLauncher
{
    public static class FontLibrary
    {
        private static PrivateFontCollection privateFonts = new PrivateFontCollection();
        private static Font dnfbitbitv2;

        public static void Initialize()
        {
            byte[] fontData = Properties.Resources.DNFBitBitv2;
            IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
            Marshal.Copy(fontData, 0, fontPtr, fontData.Length);
            privateFonts.AddMemoryFont(fontPtr, fontData.Length);
            Marshal.FreeCoTaskMem(fontPtr);

            FontFamily fontFamily = privateFonts.Families[0];
            dnfbitbitv2 = new Font(fontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Font GetFont()
        {
            if (dnfbitbitv2 == null)
            {
                Initialize();
            }
            return dnfbitbitv2;
        }
    }
}