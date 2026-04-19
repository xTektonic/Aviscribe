using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.OCR
{
    public enum OcrRegionType
    {
        AddMoon,
        RemoveMoon
    }

    public record OcrRegion(OcrRegionType Type, Rect Bounds);
}
