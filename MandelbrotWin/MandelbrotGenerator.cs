using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MandelbrotWin
{
    internal class MandelbrotGenerator
    {
        private readonly double Log2 = Math.Log(2);
        private List<int[]> palettes;

        private Func<double, double, int>[] escapeFunctions;

        // maximum number of iterations to escape
        public int MaxIterations { get; private set; } = 128;
        // the colour palette we will render with
        public int Palette { get; private set; } = 0;
        // the index into escapeFunctions of the Func which will
        // calculate the argb colour for the current coordinates
        public int EscapeFunction { get; private set; } = 0;
        // the name of the above function
        public string EscapeFunctionName
        {
            get
            {
                return escapeFunctions[EscapeFunction].Method.Name;
            }
        }

        public MandelbrotGenerator()
        {            
            // an array of delegates. each is a function which
            // calculates an argb colour. We pick the function
            // to use at runtime with the EscapeFunction index.
            escapeFunctions = new Func<double, double, int>[] {
                SmoothEscape,
                SmoothFastEscape,
                FlatEscape,
                ComplexEscape
            };

            // generate the contents of the palettes member
            GeneratePalettes();
        }

        private void GeneratePalettes()
        {
            // initialise the overall palette array.
            palettes = new List<int[]>();
            // the current palette we are populating.
            int[] palette;

            // create a palette from GetColor
            palette = new int[MaxIterations];
            for (int i = 0; i < MaxIterations; ++i)
            {
                palette[i] = GetColor(i).ToArgb();
            }
            palettes.Add(palette);

            // cribbed from ultra fractal colour scheme
            var ultraFractalpalette = new[] {
                Color.FromArgb(66, 30, 15).ToArgb(),
                Color.FromArgb(25, 7, 26).ToArgb(),
                Color.FromArgb(9, 1, 47).ToArgb(),
                Color.FromArgb(4, 4, 73).ToArgb(),
                Color.FromArgb(0, 7, 100).ToArgb(),
                Color.FromArgb(12, 44, 138).ToArgb(),
                Color.FromArgb(24, 82, 177).ToArgb(),
                Color.FromArgb(57, 125, 209).ToArgb(),
                Color.FromArgb(134, 181, 229).ToArgb(),
                Color.FromArgb(211, 236, 248).ToArgb(),
                Color.FromArgb(241, 233, 191).ToArgb(),
                Color.FromArgb(248, 201, 95).ToArgb(),
                Color.FromArgb(255, 170, 0).ToArgb(),
                Color.FromArgb(204, 128, 0).ToArgb(),
                Color.FromArgb(153, 87, 0).ToArgb(),
                Color.FromArgb(106, 52, 3).ToArgb()
            };
            // create a palette fromt he ultra fractal palette above
            palette = new int[MaxIterations];
            for (int n = 0; n < MaxIterations; n += 16)
            {
                Array.Copy(ultraFractalpalette, 0, palette, n, Math.Min(16, MaxIterations));
            }
            palettes.Add(palette);


            // create another palette from the ultra aractal colours but interpolate the 16 
            // colours across the entire palette rather than repeating it to fill
            palette = new int[MaxIterations];
            var fraction = 9;
            var increment = MaxIterations / fraction;
            var sourcePaletteIndex = 0;
            int destPaletteIndex = 0;

            while (destPaletteIndex < MaxIterations)
            {
                // get the first stop from the source palette
                var startColour = ultraFractalpalette[sourcePaletteIndex % ultraFractalpalette.Length];
                // get the second stop from the source palette
                var endColour = ultraFractalpalette[(sourcePaletteIndex + 1) % ultraFractalpalette.Length];

                // use the start colour as is for the first element of fraction
                palette[destPaletteIndex] = startColour;

                // iterate the next fraction-1 elements in the pallet, interpolating between start and end stops
                for (int i = 1; i < fraction && destPaletteIndex + i < palette.Length; i++)
                {
                    palette[destPaletteIndex + i] = LerpArgb24(startColour,
                                                    endColour,
                                                    (i * (increment / (double)fraction)) / increment);
                }

                destPaletteIndex += fraction;
                sourcePaletteIndex++;
            }
            // shift the palette to produce a nice light blue starting point
            LeftShiftArray(palette, 52); 
            palettes.Add(palette);       
        }

        public static void LeftShiftArray<T>(T[] array, int shift)
        {
            // only need to shift within the size of the array
            shift = shift % array.Length;
            // temp array to copy into 
            var buffer = new T[shift];
            // copy the first half
            Array.Copy(array, buffer, shift);
            // copy the second half
            Array.Copy(array, shift, array, 0, array.Length - shift);
            // copy the entire temp array back into the original to finish
            Array.Copy(buffer, 0, array, array.Length - shift, shift);
        }

        // nice palette generator from stack overflow
        // colour gradient:      Red -> Blue -> Green -> Red -> Black
        // corresponding values:  0  ->  16  ->  32   -> 64  ->  127 (or -1)
        static Color GetColor(int iterations)
        {
            int r, g, b;

            if (iterations < 16)
            {
                r = 16 * (16 - iterations);
                g = 0;
                b = 16 * iterations - 1;
            }
            else if (iterations < 32)
            {
                r = 0;
                g = 16 * (iterations - 16);
                b = 16 * (32 - iterations) - 1;
            }
            else if (iterations < 64)
            {
                r = 8 * (iterations - 32);
                g = 8 * (64 - iterations) - 1;
                b = 0;
            }
            else
            { // range is 64 - 127
                r = 255 - (iterations - 64) * 4;
                g = 0;
                b = 0;
            }
            return Color.FromArgb(255, Math.Max(0, Math.Min(r, 255)), Math.Max(0, Math.Min(g, 255)), Math.Max(0, Math.Min(b, 255)));
        }

        /// <summary>
        /// Main driver function for the generator
        /// </summary>
        /// <param name="zoom">how far into the mandelbrot we are to zoom</param>
        /// <param name="offsetX">horizontal offset along the complex plane</param>
        /// <param name="offsetY">vertical offset along the complex plane</param>
        /// <param name="destination">the bitmap we write to</param>
        public void Render(double zoom, double offsetX, double offsetY, Bitmap destination)
        {
            // hold the dimensions in variables because the bitmap properties cannot be 
            // accessed across threads in the parallel foreach below
            int width = destination.Width;
            int height = destination.Height;

            // The buffer we will render our 32bbp output into. 
            // We dont render directly to the destination Bitmap because we are
            // rendering with multiple threads and bitmaps dont allow cross thread
            // access. Each int is one pixel in argb format
            int[] buffer = new int[width * height];

            // get a set of independant rectangular regions which we can render to across threads.
            IEnumerable<Rectangle> ranges = CreateChunkedRenderRegions(width, height);

            // iterate the sub regions concurrently, rendering each independently
            Parallel.ForEach(ranges, (imageRange, state, count) =>
            {
                // Dispatch to a function which will render a subsection of the mandelbrot 
                // to our output array
                RenderSubWindow(zoom, offsetX, offsetY,
                            buffer, width, height,
                            imageRange);
            });

            // lock the bitmap so that we can bulk update the data within it
            var rect = new Rectangle(0, 0, destination.Width, destination.Height);
            var data = destination.LockBits(rect, ImageLockMode.ReadWrite, destination.PixelFormat);
            // copy our rendered output into the bitmap
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            // unlock the bitmap to complete the transfer
            destination.UnlockBits(data);
        }

        /// <summary>
        /// Takes a 2d size and returns a set of non overlapping rectangles which cover that area
        /// </summary>
        /// <param name="width">required width</param>
        /// <param name="height">required height</param>
        /// <returns>IEnumerable of Rectangles to later iterate</returns>
        private IEnumerable<Rectangle> CreateChunkedRenderRegions(int width, int height)
        {
            var (xTiles, yTiles) = ComputeSubWindow(width, height);
            var ranges = BuildSubWindows(xTiles, yTiles, width, height);
            return ranges;
        }

        /// <summary>
        /// Takes a two dimensional area and returns a total number of Rectangles in each dimension
        /// </summary>
        /// <param name="width">required width</param>
        /// <param name="height">required height</param>
        /// <returns>Tuple of total x & y Rect count</returns>
        private static (int xTiles, int yTiles) ComputeSubWindow(int width, int height)
        {
            // calculate how many chunks we need in total.
            int chunks = Math.Max(32 * Environment.ProcessorCount, (width * height) / (64 * 64));
            // lets have a power of two.
            chunks = (int)RoundUpPow2((uint)chunks);

            // now calculate a x,y counts such that x*y=chunks
            int dx = width;
            int dy = height;

            int nx = chunks, ny = 1;

            while ((nx & 0x1) == 0 && 2 * dx * ny < dy * nx)
            {
                nx >>= 1;
                ny <<= 1;
            }

            // post condition
            Debug.Assert(nx * ny == chunks);

            return (nx, ny);
        }

        /// <summary>
        /// Takes an integer and returns the next power of two above it.
        /// It sets all the bits below the highest one and then adds one to roll up to a power of two.
        /// </summary>
        public static UInt32 RoundUpPow2(UInt32 v)
        {
            v--;

            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;

            return v + 1;
        }

        /// <summary>
        /// Returns an IEnumerable of Rectanges which together cover a set rectangular area.
        /// </summary>
        /// <param name="xTiles">required number of horizontal tiles</param>
        /// <param name="yTiles">required number of vertical tiles</param>
        /// <param name="width">total horizonntal pixels</param>
        /// <param name="height">total vertical pixels</param>
        /// <returns>xTiles * yTiles rectangles which cover a width * height area</returns>
        private IEnumerable<Rectangle> BuildSubWindows(int xTiles, int yTiles, int width, int height)
        {
            int total = xTiles * yTiles;
            int nx = xTiles;
            int ny = yTiles;

            for (int i = 0; i < total; i++)
            {
                // Compute $x$ and $y$ pixel sample range for sub-window
                var xo = i % nx;
                var yo = i / nx;

                var tx0 = (float)xo / (float)nx;
                var tx1 = ((float)xo + 1) / (float)nx;
                var ty0 = (float)yo / (float)ny;
                var ty1 = ((float)yo + 1) / (float)ny;

                var x1 = (int)Math.Floor(Lerp(0, width, tx0));
                var x2 = (int)Math.Floor(Lerp(0, width, tx1));
                var y1 = (int)Math.Floor(Lerp(0, height, ty0));
                var y2 = (int)Math.Floor(Lerp(0, height, ty1));

                yield return new Rectangle(x1, y1, x2 - x1, y2 - y1);
            }
        }

        /// <summary>
        /// Interpolate between to double numbers.
        /// </summary>
        /// <param name="a">start number, can be any value</param>
        /// <param name="b">end number, can be any value</param>
        /// <param name="t">ratio between a & b. should be between 0-1</param>
        /// <returns></returns>
        public static double Lerp(double a, double b, double t)
        {
            return ((a) + (t) * ((b) - (a)));
        }

        /// <summary>
        /// interpolate between two int32 bit numbers.
        /// The numbers are interpreted to be four 8 bit
        /// argb colours. The alpha component is not interpolated
        /// but is instead explicitly set to 255.
        /// </summary>
        /// <param name="c1">start colour</param>
        /// <param name="c2">end colour</param>
        /// <param name="t">ratio between c1 & c2. should between 0-1</param>
        /// <returns>an argb colour some way between c1 & c2 with the high 8 bits set to 1</returns>
        public static int LerpArgb24(int c1, int c2, double t)
        {
            var r = (int)Lerp(c1 & 0xFF0000, c2 & 0xFF0000, t) & 0xFF0000;
            var g = (int)Lerp(c1 & 0xFF00,   c2 & 0xFF00,   t) & 0xFF00;
            var b = (int)Lerp(c1 & 0xFF,     c2 & 0xFF,     t) & 0xFF;

            // construct 32bit int argb output
            var col =
                255 << 24 |
                r |
                g |
                b;

            return col;
        }

        /// <summary>
        /// Main worker function. It actually renders a rectangular
        /// sub region of the main output image array.
        /// </summary>
        /// <param name="zoom">depth into the fractal to render at</param>
        /// <param name="offsetX">horizontal position in the complex plane</param>
        /// <param name="offsetY">vertical position in the complex plane</param>
        /// <param name="image">destination int array to render to</param>
        /// <param name="width">width of the output image in pixels</param>
        /// <param name="height">height of the output image in pixels</param>
        /// <param name="imageRange">sub region of output image to render to in pixels</param>
        void RenderSubWindow(double zoom, double offsetX, double offsetY,
            int[] image, int width, int height,
            Rectangle imageRange)
        {
            int minX = imageRange.Left;
            int maxX = imageRange.Right;
            int minY = imageRange.Top;
            int maxY = imageRange.Bottom;

            // get the function which will calculate the colour at a position
            // in the complex plane
            var escape = escapeFunctions[EscapeFunction];

            // calculate the start position (x,y) in the complex plane
            // based on the requested subwindow range, the zoom and the x y offsets
            // we have been given
            double realstart = minX * zoom - width / 2.0 * zoom + offsetX;
            double imagstart = minY * zoom - height / 2.0 * zoom + offsetY;

            // iterate over the xy region in both pixels and complex coordinates
            double imag = imagstart;
            for (int y = minY; y < maxY; y++, imag += zoom)
            {
                double real = realstart;
                for (int x = minX; x < maxX; x++, real += zoom)
                {
                    // index the output array as pixels
                    int pixelOffset = (y * width) + (x);

                    // calculate the output colour with complex coordinates
                    int colour = escape(imag, real);

                    // store the calculated colour in our output array
                    image[pixelOffset] = colour;
                }
            }
        }

        #region Rendering Functions
        // produces a smooth fractal but has some artifacts due to
        // using rearranged math, the gradients have some discontinuities.
        private int SmoothFastEscape(double imag, double real)
        {
            var value = MandelbrotFractionalEscapeTime(real, imag);            
            return EscapeTimeToSmoothColour(value);
        }

        private double MandelbrotFractionalEscapeTime(double cr, double ci)
        {            
            var zr = cr;
            var zi = ci;

            for (int counter = 0; counter < MaxIterations; counter++)
            {
                double r2 = zr * zr;
                double i2 = zi * zi;

                if (r2 + i2 > 4.0)
                {
                    var log_zn = Math.Log(r2 + i2) / 2.0;
                    var nu = Math.Log(log_zn / Math.Log(2)) / Math.Log(2);

                    return counter + 1 - nu;                    
                }

                zi = 2.0 * zr * zi + ci;
                zr = r2 - i2 + cr;
            }

            return (double)MaxIterations - 1;
        }

        // produces a smooth fractal with no discontinuities but suffers from slower performance
        // doesnt use System.Numerics
        private int SmoothEscape(double imag, double real)
        {
            var value = MandelbrotFractionalEscapeTimeSlow(real, imag);
            return EscapeTimeToSmoothColour(value);
        }

        private double MandelbrotFractionalEscapeTimeSlow(double cr, double ci)
        {
            var zr = cr;
            var zi = ci;

            for (int counter = 0; counter < MaxIterations; counter++)
            {
                double r2 = zr * zr;
                double i2 = zi * zi;

                var zrNew = r2 - i2 + cr;
                var ziNew = (zi * zr + zr * zi) + ci;                

                zr = zrNew;
                zi = ziNew;

                if (ComplexAbs(zr, zi) > 4.0)
                {
                    var log_zn = Math.Log(zr * zr + zi * zi) / 2.0;
                    var nu = Math.Log(log_zn / Math.Log(2)) / Math.Log(2);

                    return counter + 1 - nu;
                }
            }

            return (double)MaxIterations - 1;
        }

        /// <summary>
        /// pulled out of system.numerics with ilspy. Just used for comparison purposes with the faster 
        /// but less pretty versions. 
        /// both the implementations that use this function give a nicer, smoother, gradient.
        /// The sqrt makes it slower slower to use however.
        /// 
        /// The reason this is so seemingly complex is down to numerical stability. This returns the same as
        /// Math.Sqrt(zr*zr + zi*zi) but that runs the right of overflow and lack of precision in the squaring.
        /// The formula here achieves the same result but without the problems. See Numerical Recipes in C
        /// for more information
        /// </summary>
        double ComplexAbs(double zr, double zi)
        {
            if (double.IsInfinity(zr) || double.IsInfinity(zi))
                return double.PositiveInfinity;
            
            double zr_abs = Math.Abs(zr);
            double zi_abs = Math.Abs(zi);

            if (zr_abs > zi_abs)
            {
                double t = zi_abs / zr_abs;
                return zr_abs * Math.Sqrt(1.0 + t * t);
            }

            if (zi_abs == 0.0)
            {
                return zr_abs;
            }
            else
            {
                double t = zr_abs / zi_abs;
                return zi_abs * Math.Sqrt(1.0 + t * t);
            }
        }

        // the fastest implmentation. flat shaded and quick to navigate around with.
        private int FlatEscape(double imag, double real)
        {
            var value = MandelbrotEscapeTime(real, imag);
            return EscapeTimeToFlatColour(value);
        }      

        private int MandelbrotEscapeTime(double cr, double ci)
        {
            double zr = cr;
            double zi = ci;

            for (int counter = 0; counter < MaxIterations; ++counter)
            {
                double r2 = zr * zr;
                double i2 = zi * zi;

                if (r2 + i2 > 4.0)
                    return counter;

                zi = 2.0 * zr * zi + ci;
                zr = r2 - i2 + cr;
            }

            return MaxIterations - 1;
        }

        // Using System.Numerics.Complex type with textbook maths
        // as pretty at the SmoothEscape above, but even slower.
        // Included for comparison purposes.
        private int ComplexEscape(double imag, double real)
        {
            var value = MandelbrotComplex(new Complex(real, imag));
            return EscapeTimeToSmoothColour(value);
        }
    
        private double MandelbrotComplex(Complex c)
        {
            int iterations = 0;
            var z = c;
            var max = MaxIterations;

            while (iterations < max - 1 && z.Magnitude <= 4.0)
            {
                z = z * z + c;
                iterations++;
            }

            if (iterations < max - 1)
            {
                var log_zn = Math.Log(z.Real * z.Real + z.Imaginary * z.Imaginary) / 2.0;
                var nu = Math.Log(log_zn / Math.Log(2)) / Math.Log(2);

                // we dont need to add one here because the above while loop will generally count at least one
                return iterations /* + 1*/ - nu;
            }

            return iterations;
        }

        /// <summary>
        /// Just grab a flat shaded colour from the pallete
        /// </summary>
        /// <param name="value">orbital escape value</param>
        /// <returns>argb color encoded into an Int32</returns>
        private int EscapeTimeToFlatColour(int value)
        {
            return palettes[Palette][value];
        }

        /// <summary>
        /// Interpolated colour between two stops in the palette
        /// </summary>
        /// <param name="value">orbital escape value</param>
        /// <returns>argb color encoded into an Int32</returns>
        private int EscapeTimeToSmoothColour(double value)
        {
            if (value < 0)
                value = 0;

            if (value >= palettes[Palette].Length)
                value = palettes[Palette].Length - 1;

            var coloura = palettes[Palette][(int)value];
            var colourb = value >= MaxIterations - 1 ? coloura : palettes[Palette][(int)(value) + 1];

            var t = value - Math.Truncate(value);

            var r = (int)Lerp(coloura & 0xFF0000, colourb & 0xFF0000, t) & 0xFF0000;
            var g = (int)Lerp(coloura & 0xFF00, colourb & 0xFF00, t) & 0xFF00;
            var b = (int)Lerp(coloura & 0xFF, colourb & 0xFF, t) & 0xFF;

            // construct argb
            var col =
                255 << 24 |
                r |
                g |
                b;

            return col;
        }
        #endregion

        /// <summary>
        /// Moves to the next colour calculation function
        /// </summary>
        internal void NextEscapeFunction()
        {
            EscapeFunction = (EscapeFunction + 1) % escapeFunctions.Length;
        }

        /// <summary>
        /// Moves to the next colour palette
        /// </summary>
        internal void NextPalette()
        {
            Palette = (Palette + 1) % (palettes.Count);
        }
        
        /// <summary>
        /// Rotates the current colour palette one place to the left
        /// </summary>
        internal void RotatePalette()
        {
            LeftShiftArray(palettes[Palette], 1);
        }

        /// <summary>
        /// Increases or decreases the maximum number of iterations to use
        /// when calculating the colour of a position in the fractal
        /// </summary>        
        internal void Iterations(bool increase = true)
        {
            if (increase)
                MaxIterations = MaxIterations * 2;
            else
                MaxIterations = MaxIterations / 2;

            MaxIterations = Math.Max(MaxIterations, 1);

            GeneratePalettes();
        }
    }
}