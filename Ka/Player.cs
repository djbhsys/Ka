using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ka
{
    using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Media;
        using System.Windows.Shapes;
        using Microsoft.Kinect;
        using Ka.Utils;
        using Coding4Fun.Kinect.Wpf;

        public class Player
        {
            private const double BoneSize = 0.01;
            private const double HeadSize = 0.075;
            private const double HandSize = 0.03;
            private const float FeetPerMeters = 3.2808399f;

            // Keeping track of all bone segments of interest as well as head, hands and feet
            private readonly Dictionary<Bone, BoneData> segments = new Dictionary<Bone, BoneData>();
            private readonly int id;
            private Rect playerBounds;
            private System.Windows.Point playerCenter;
            private double playerScale;

            public Player(int skeletonSlot)
            {
                this.id = skeletonSlot;

                this.LastUpdated = DateTime.Now;
            }

            public bool IsAlive { get; set; }

            public DateTime LastUpdated { get; set; }

            public Dictionary<Bone, BoneData> Segments
            {
                get
                {
                    return this.segments;
                }
            }

            //手位置相关
            public void TrackHand(Point point, Joint hand, FrameworkElement cursorElement, bool isLeft)
            {
                if (hand.TrackingState != JointTrackingState.NotTracked)
                {
                    double z = hand.Position.Z * FeetPerMeters;
                    cursorElement.Visibility = Visibility.Visible;
                    Point jointPoint = point;
                    Canvas.SetLeft(cursorElement, jointPoint.X);
                    Canvas.SetTop(cursorElement, jointPoint.Y);
                    Canvas.SetZIndex(cursorElement, (int)(1200 - (z * 100)));

                }
            }


            private static Point GetJointPoint(KinectSensor kinectDevice, Joint joint, Size containerSize, Point offset)
            {
                DepthImagePoint point = kinectDevice.MapSkeletonPointToDepth(joint.Position, kinectDevice.DepthStream.Format);

                point.X = (int)((point.X * containerSize.Width / kinectDevice.DepthStream.FrameWidth) - offset.X + 0.5f);
                point.Y = (int)((point.Y * containerSize.Height / kinectDevice.DepthStream.FrameHeight) - offset.Y + 0.5f);

                return new Point(point.X, point.Y);
            }

            public int GetId()
            {
                return this.id;
            }

            public void SetBounds(Rect r)
            {
                this.playerBounds = r;
                this.playerCenter.X = (this.playerBounds.Left + this.playerBounds.Right) / 2;
                this.playerCenter.Y = (this.playerBounds.Top + this.playerBounds.Bottom) / 2;
                this.playerScale = Math.Min(this.playerBounds.Width, this.playerBounds.Height / 2);
            }



            public Point UpdateJointPosition(Microsoft.Kinect.JointCollection joints, JointType j, double ActualWidth, double ActualHeight)
            {
                var nuiv = joints[j].ScaleTo((int)ActualWidth, (int)ActualHeight, 0.50f, 0.30f).Position;
                return new Point(nuiv.X, nuiv.Y);
            }

            public Boolean ShackHandCheck(SkeletonPoint hand, SkeletonPoint elbow)
            {
                DateTime start = System.DateTime.Now;
                DateTime end = System.DateTime.Now;
                Boolean get = true;
                int count =0;

                while(count < 5)
                {
                    if (end.Second-start.Second>4||end.Minute!=start.Minute)
                    {
                        return false;
                    }

                    if (get)
                    {
                        if(hand.X-elbow.X > 0.3 || hand.X-elbow.X < -0.3){
                            get = true;
                            count++;
                        }else{
                            get = false;
                        }
                    }
                }
                return true;
                
            }
        }
    
}
