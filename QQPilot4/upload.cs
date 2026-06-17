using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace QSummaryCore
{
    //[DllImport(]


    internal class Upload
    {
        [DllImport("uploadFile.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int upload();
        //extern "C" int __declspec(dllexport) upload()
    }
}
