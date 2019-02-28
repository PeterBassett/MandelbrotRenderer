using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MandelbrotWin
{
    public partial class Main : Form
    {
        // How much to zoom in/out in one step
        private const double ZoomMultiplier = 0.9;
        // how far to move when navigating by the keyboard
        private const int MovementMultiplier = 40;
        // current position and zoom level to render at
        private double zoom;
        private double offsetY;
        private double offsetX;

        // Our fractal renderer
        private MandelbrotGenerator generator;
        // the render target which is given to the generator
        // this allows us to render across multiple threads
        private Bitmap renderTarget;
        
        // Are we currently dragging the display to navigate?
        private bool dragging;        
        // If dragging, where in the window did the last event occur?
        private Point lastDragPosition;
        // should we display text information about stats etc?
        private bool drawInformation;

        public Main()
        {            
            InitializeComponent();

            // MouseWheel event is not browsable, lets set it up here.
            this.MouseWheel += Main_MouseWheel;
            // setup our drawing config
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);

            // initial position to view from
            offsetX = -0.7;
            offsetY = 0.0;
            zoom = 0.004;
            // another interesting point to zoom straight into
            //offsetX = -1.8623310905588983;
            //offsetY = 1.3995652869186738E-15;

            drawInformation = true;            

            generator = new MandelbrotGenerator();
            renderTarget = new Bitmap(Width, Height);
        }

        private void Main_Paint(object sender, PaintEventArgs e)
        {            
            // time how long our rendering takes
            var sw = Stopwatch.StartNew();
            // do the actual rendering
            generator.Render(zoom, offsetX, offsetY, renderTarget);           
            sw.Stop(); 

            // draw the resultant graphic to the window
            e.Graphics.DrawImage(renderTarget, 0, 0, renderTarget.Width, renderTarget.Height);

            // optionally draw stats and instructions
            if (drawInformation)
            {
                var elapsedTime = string.Format("{0:0}fps, escape time algorithm {1}, palette {2}, max iterations {3}.\r\nwasd to move, +- or mouse wheel to zoom.\r\ne for escape function. p to change palette. r to rotate palette.\r\n<> to change iteration depth\r\ni for information.",
                    Math.Min(1000.0 / sw.ElapsedMilliseconds, 1000.0),
                    generator.EscapeFunctionName,
                    generator.Palette,
                    generator.MaxIterations);

                e.Graphics.DrawString(elapsedTime, this.Font, Brushes.Black, new Point(12, 12));
                e.Graphics.DrawString(elapsedTime, this.Font, Brushes.White, new Point(10, 10));
            }
        }

        /// <summary>
        /// Handle various keys which allow the user to interact with the plot
        /// </summary>
        private void Main_KeyPress(object sender, KeyPressEventArgs e)
        {
            var keyChar = char.ToLower(e.KeyChar);

            switch (keyChar)
            {
                case '=':
                    zoom *= ZoomMultiplier;
                    break;
                case '-':
                    zoom /= ZoomMultiplier;
                    break;
                case 'w':
                    offsetY -= MovementMultiplier * zoom;
                    break;
                case 's':
                    offsetY += MovementMultiplier * zoom;
                    break;
                case 'a':
                    offsetX -= MovementMultiplier * zoom;
                    break;
                case 'd':
                    offsetX += MovementMultiplier * zoom;
                    break;
                case 'r':
                    generator.RotatePalette();
                    break;
                case 'p':
                    generator.NextPalette();
                    break;
                case 'e':
                    generator.NextEscapeFunction();
                    break;
                case 'i':
                    drawInformation = !drawInformation;
                    break;
                case '<':
                case ',':
                    generator.Iterations(false);
                    break;
                case '>':
                case '.':
                    generator.Iterations(true);
                    break;
            }

            Invalidate();
        }

        /// <summary>
        /// When the window resizes we need to create a new off screen buffer
        /// to render to.
        /// </summary>
        private void Main_Resize(object sender, EventArgs e)
        {
            renderTarget = new Bitmap(Width, Height);
            Invalidate();
        }

        #region Dragging
        private void Main_MouseDown(object sender, MouseEventArgs e)
        {
            dragging = true;
            lastDragPosition = e.Location;
        }

        private void Main_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
                return;

            var delta = new Point(lastDragPosition.X - e.Location.X,
                                  lastDragPosition.Y - e.Location.Y);

            lastDragPosition = e.Location;

            offsetX += delta.X * zoom;
            offsetY += delta.Y * zoom;

            Invalidate();
        }

        private void Main_MouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;

            Invalidate();
        }

        private void Main_MouseWheel(object sender, MouseEventArgs e)
        {
            var delta = e.Delta / 120;

            if(delta > 0)
                zoom *= ZoomMultiplier;
            else
                zoom /= ZoomMultiplier;

            Invalidate();
        }
        #endregion
    }
}