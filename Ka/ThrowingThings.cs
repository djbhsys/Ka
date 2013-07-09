using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ka
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Shapes;
    using Microsoft.Kinect;
    using Ka.Utils;
    using System.Windows.Media.Imaging;

    public class ThrowingThings
    {
        private const double BaseGravity = 0.010;//0.017
        private const double BaseAirFriction = 0.994;

        private readonly List<Thing> things = new List<Thing>();
        private readonly Random rnd = new Random();
        private readonly int intraFrames = 1;
        private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
        private const double DissolveTime = 0.4;
        private Rect sceneRect;
        private double targetFrameRate = 60;
        private double dropRate = 2.0;
        private GameMode gameMode = GameMode.Off;
        private double gravity = BaseGravity;
        private double gravityFactor = 1.0;
        private double airFriction = BaseAirFriction;
        private int frameCount;
        private double expandingRate = 1.0;
        private DateTime gameStartTime;
        private int maxThings = 2;//最多同时出现一个物体。
        private BitmapImage boyImage = new BitmapImage();
        // BitmapImage.UriSource must be in a BeginInit/EndInit block.
        //private ImageBrush boy;

        FallingThings falling;

        public ThrowingThings(FallingThings falling, double framerate, int intraFrames)
        {
            boyImage.BeginInit();
            boyImage.UriSource = new Uri(@"/code/Ka/Ka/Resources/throwingBoy.png", UriKind.RelativeOrAbsolute);
            boyImage.EndInit();
            this.falling = falling;
            this.intraFrames = intraFrames;
            this.targetFrameRate = framerate * intraFrames;
            this.SetGravity(this.gravityFactor);
            this.sceneRect.X = this.sceneRect.Y = 0;
            this.sceneRect.Width = 1152;
            this.sceneRect.Height = 648;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
            this.DropNewThing(boyImage);
        }

        public enum ThingState
        {
            Falling = 0,
            Bouncing = 1,
            Dissolving = 2,
            Remove = 3
        }


        private Path MakeSimpleBoy(BitmapImage boyImage, System.Windows.Point left, ImageBrush boy)
        {
            Rect rect = new Rect(left.X, left.Y, boyImage.Height, boyImage.Width);//其高度的单位还不是很清楚

            RectangleGeometry myRectangleGeometry = new RectangleGeometry();
            myRectangleGeometry.Rect = rect;

            GeometryGroup myGeometryGroup = new GeometryGroup();
            myGeometryGroup.Children.Add(myRectangleGeometry);

            Path myPath = new Path();
            myPath.Data = myGeometryGroup;
            myPath.Fill = boy;

            return myPath;
        }

        public void SetFramerate(double actualFramerate)
        {
            this.targetFrameRate = actualFramerate * this.intraFrames;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
            if (this.gravityFactor != 0)
            {
                this.SetGravity(this.gravityFactor);
            }
        }

        public void SetBoundaries(Rect r)
        {
            this.sceneRect = r;
        }

        public void SetDropRate(double f)
        {
            this.dropRate = f;
        }

        public void Reset()
        {
            for (int i = 0; i < this.things.Count; i++)
            {
                Thing thing = this.things[i];
                if ((thing.State == ThingState.Bouncing) || (thing.State == ThingState.Falling))
                {
                    thing.State = ThingState.Dissolving;
                    thing.Dissolve = 0;
                    this.things[i] = thing;
                }
            }

            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void SetGameMode(GameMode mode)
        {
            this.gameMode = mode;
            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void SetGravity(double f)
        {
            this.gravityFactor = f;
            this.gravity = f * BaseGravity / this.targetFrameRate / Math.Sqrt(this.targetFrameRate) / Math.Sqrt(this.intraFrames);
            this.airFriction = f == 0 ? 0.997 : Math.Exp(Math.Log(1.0 - ((1.0 - BaseAirFriction) / f)) / this.intraFrames);

            if (f == 0)
            {
                // Stop all movement as well!
                for (int i = 0; i < this.things.Count; i++)
                {
                    Thing thing = this.things[i];
                    thing.XVelocity = thing.YVelocity = 0;
                    this.things[i] = thing;
                }
            }
        }

        public void AdvanceFrame()
        {
            // Move all things by one step, accounting for gravity
            for (int thingIndex = 0; thingIndex < this.things.Count; thingIndex++)
            {
                Thing thing = this.things[thingIndex];
                thing.Left.Offset(thing.XVelocity, thing.YVelocity);
                thing.YVelocity += this.gravity * this.sceneRect.Height;
                thing.YVelocity *= this.airFriction;
                thing.XVelocity *= this.airFriction;
                thing.Theta += thing.SpinRate;

                // bounce off walls
                if ((thing.Left.X < 0) || (thing.Left.X + thing.Width > this.sceneRect.Width))
                {
                    thing.XVelocity = -thing.XVelocity;
                    thing.Left.X += thing.XVelocity;
                }

                // Then get rid of one if any that fall off the bottom
                if (thing.Left.Y - thing.Height > this.sceneRect.Bottom)
                {
                    thing.State = ThingState.Remove;
                }

                // Get rid of after dissolving.
                if (thing.State == ThingState.Dissolving)
                {
                    thing.Dissolve += 1 / (this.targetFrameRate * DissolveTime);
                    thing.Width *= this.expandingRate;
                    thing.Height += this.expandingRate;
                    if (thing.Dissolve >= 1.0)
                    {
                        thing.State = ThingState.Remove;
                    }
                }

                this.things[thingIndex] = thing;
            }

            // Then remove any that should go away now
            for (int i = 0; i < this.things.Count; i++)
            {
                Thing thing = this.things[i];
                if (thing.State == ThingState.Remove)
                {
                    this.things.Remove(thing);
                    i--;
                }
            }

            // Create any new things to drop based on dropRate
            if ((this.things.Count < this.maxThings) && (this.rnd.NextDouble() < this.dropRate / this.targetFrameRate))
            {
                this.DropNewThing(boyImage);
            }
        }

        public void DrawFrame(UIElementCollection children)
        {
            this.frameCount++;
            if (this.things.Count >= 1)
            {
                // Draw the thowing boy
                Thing thing = this.things[0];
                if (thing.boy == null)
                {
                    thing.boy = new ImageBrush(this.boyImage);
                }

                children.Add(
                    this.MakeSimpleBoy(
                        boyImage,
                        thing.Left,
                        thing.boy));
            }
        }

        private void DropNewThing(BitmapImage image)
        {
            // Only drop within this area 
            double dropHeight = this.sceneRect.Bottom - this.sceneRect.Top;
            double dropWidth = this.sceneRect.Right - this.sceneRect.Left;

            var newThing = new Thing
            {
                // Left = new System.Windows.Point(this.sceneRect.Left - image.Width , (this.rnd.NextDouble() * dropWidth) + ((this.sceneRect.Top + this.sceneRect.Bottom - dropHeight) / 2)),
                Left = new System.Windows.Point(0, 0),
                Width = image.Width,
                Height = image.Height,
                Theta = 0,
                SpinRate = ((this.rnd.NextDouble() * 12.0) - 6.0) * 2.0 * Math.PI / this.targetFrameRate / 4.0,
                YVelocity =   (((0.5 * this.rnd.NextDouble()) - 0.25) / this.targetFrameRate),//        待修改
                XVelocity = 2,
                boy = new ImageBrush(image),
                Dissolve = 0,
                State = ThingState.Falling,
                TouchedBy = 0,
                Hotness = 0,
                FlashCount = 0
            };

            this.things.Add(newThing);
        }

        #region get point, width ,height

        public Point GetThingPoint()
        {
            return this.things[0].Left;
        }

        public double GetWidth()
        {
            return this.things[0].Width;
        }

        public double GetHeight()
        {
            return this.things[0].Height;
        }
        #endregion

        // The Thing struct represents a single object that is flying through the air, and
        // all of its properties.
        private struct Thing
        {
            public System.Windows.Point Left;
            public double Width;
            public double Height;
            public double Theta;
            public double SpinRate;
            public double YVelocity;
            public double XVelocity;
            public ImageBrush boy;
            public double Dissolve;
            public ThingState State;
            public int TouchedBy;               // Last player to touch this thing
            public int Hotness;                 // Score level
            public int FlashCount;
        }
    }
}
