using Microsoft.Win32;
using SharpAvi.Output;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ImageBitMixr
{
    /// TODO Do a clustered kind of dithering where it looks like low res dithering
    /// on high res pictures. So it could be used for a low-fi look on HD.


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        struct SourceImage
        {
            public float[,] pixelBits;
            public int pixelBitsSubSampling; // multiple of 2
            public float ratio;
            public Bitmap srcImage;
        }
        SourceImage[] imagesToMix = new SourceImage[2];

        Bitmap resultBitmap = null;

        LinearAccessByteImageUnsignedHusk imgHusk = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private float[,] byteArrayToPixelBitsFloatArray(ref byte[] input,int extraLength) {

            int bitDepth = 8;

            float[,] retVal = new  float[input.Length + extraLength, bitDepth]; 


            for (int i = 0; i < input.Length; i++)
            {

                for (int b = 0; b < bitDepth; b++)
                {
                    retVal[i, b] = 0b000_0001 & (input[i] >> b);
                }

            }
            return retVal;
        } 

        private byte[] pixelBitsFloatArrayToByteArray(ref float[,] input) {

            int length = input.GetLength(0);
            int bitDepth = input.GetLength(1);

            byte[] retVal = new  byte[length]; 


            for (int i = 0; i < retVal.Length; i++)
            {

                for (int b = 0; b < bitDepth; b++)
                {
                    retVal[i] |= (byte)(
                        ((int)
                        Math.Max(0.0f,Math.Min(1.0f,Math.Round(input[i,b])))
                        )<<b
                        );
                }

            }
            return retVal;
        } 



        private void btnLoadImage1_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Title = "Select image 1";
            if(ofd.ShowDialog() == true)
            {
                if (imagesToMix[0].srcImage != null)
                {
                    imagesToMix[0].srcImage.Dispose();
                }

                Bitmap image = (Bitmap)Bitmap.FromFile(ofd.FileName);

                LinearAccessByteImageUnsignedNonVectorized img = LinearAccessByteImageUnsignedNonVectorized.FromBitmap(image);

                imgHusk = img.toHusk();

                SourceImage tmp = new SourceImage() { srcImage=image, pixelBits = byteArrayToPixelBitsFloatArray(ref img.imageData, img.width * 3+3), ratio=0.5f  }; // we add img.width*3+3 becasue the dithering needs an extra line and pixel afair.
                    
                imagesToMix[0] = tmp;

                img1.Source = Helpers.BitmapToImageSource(image);
                
            }
        }

        private void btnLoadImage2_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Title = "Select image 2";
            if (ofd.ShowDialog() == true)
            {
                if(imagesToMix[1].srcImage != null)
                {
                    imagesToMix[1].srcImage.Dispose();
                }

                Bitmap image;
                using (Bitmap tmpBitmap = (Bitmap)Bitmap.FromFile(ofd.FileName))
                {
                    if(tmpBitmap.Width == imagesToMix[0].srcImage.Width && tmpBitmap.Height == imagesToMix[0].srcImage.Height)
                    {
                        image = (Bitmap)tmpBitmap.Clone();

                    }
                    else
                    {
                        image = Helpers.ResizeBitmapHQ(tmpBitmap, imagesToMix[0].srcImage.Width, imagesToMix[0].srcImage.Height);

                    }
                }

                LinearAccessByteImageUnsignedNonVectorized img = LinearAccessByteImageUnsignedNonVectorized.FromBitmap(image);

                SourceImage tmp = new SourceImage() { srcImage = image, pixelBits = byteArrayToPixelBitsFloatArray(ref img.imageData, img.width * 3+3), ratio = 0.5f }; // we add img.width*3+3 becasue the dithering needs an extra line and pixel afair.

                imagesToMix[1] = tmp;

                img2.Source = Helpers.BitmapToImageSource(image);
                
            }
        }

        private void btnDoMix_Click(object sender, RoutedEventArgs e)
        {
            DoMix();
        }

        private void DoMix()
        {


            if (resultBitmap != null)
            {
                resultBitmap.Dispose();
            }
            resultBitmap = getMixedImage();
            imgResult.Source = Helpers.BitmapToImageSource(resultBitmap);
        }

        private Bitmap getMixedImage()
        {
            byte[] asBytes = getMixedImageByteArray();
            LinearAccessByteImageUnsignedNonVectorized resultImg = new LinearAccessByteImageUnsignedNonVectorized(asBytes, imgHusk);

            return resultImg.ToBitmap();
        }

        private byte[] getMixedImageByteArray()
        {
            float[,] tmpArrayForDithering = new float[imagesToMix[0].pixelBits.GetLength(0), imagesToMix[0].pixelBits.GetLength(1)];

            int bitDepth = tmpArrayForDithering.GetLength(1);

            // Prepare mixed array.
            Parallel.For(0, bitDepth, (int b) =>
            {
                for (int i = 0; i < tmpArrayForDithering.GetLength(0); i++)
                {

                    //for (int b = 0; b < bitDepth; b++)
                    //{
                    tmpArrayForDithering[i, b] += imagesToMix[0].pixelBits[i, b] * imagesToMix[0].ratio;
                    tmpArrayForDithering[i, b] += imagesToMix[1].pixelBits[i, b] * imagesToMix[1].ratio;
                    //}

                }
            });
            int pseudoStride = imagesToMix[0].srcImage.Width * 3;
            int actualImageLength = imagesToMix[0].srcImage.Width * imagesToMix[0].srcImage.Height * 3;


            Parallel.For(0, bitDepth, (int b) =>
            {
                for (int i = 0; i < actualImageLength; i++)
                {

                    float oldPixel = tmpArrayForDithering[i, b];
                    float newPixel = oldPixel >= 0.5 ? 1 : 0;
                    tmpArrayForDithering[i, b] = newPixel;
                    float quant_error = oldPixel - newPixel;

                    tmpArrayForDithering[i + 3, b] = tmpArrayForDithering[i + 3, b] + quant_error * 7.0f / 16.0f;
                    tmpArrayForDithering[i - 3 + pseudoStride, b] = tmpArrayForDithering[i - 3 + pseudoStride, b] + quant_error * 3.0f / 16.0f;
                    tmpArrayForDithering[i + pseudoStride, b] = tmpArrayForDithering[i + pseudoStride, b] + quant_error * 5.0f / 16.0f;
                    tmpArrayForDithering[i + 3 + pseudoStride, b] = tmpArrayForDithering[i + 3 + pseudoStride, b] + quant_error * 1.0f / 16.0f;


                }
            });
            // Dither
            /*for (int i = 0; i < actualImageLength; i++)
            {

                for (int b = 0; b < bitDepth; b++)
                {
                    float oldPixel = tmpArrayForDithering[i, b];
                    float newPixel = oldPixel >= 0.5 ? 1 : 0;
                    tmpArrayForDithering[i, b] = newPixel;
                    float quant_error = oldPixel - newPixel;

                    tmpArrayForDithering[i + 3, b] = tmpArrayForDithering[i + 3, b] + quant_error * 7.0f / 16.0f;
                    tmpArrayForDithering[i-3+pseudoStride, b] = tmpArrayForDithering[i - 3 + pseudoStride, b] + quant_error * 3.0f / 16.0f;
                    tmpArrayForDithering[i+pseudoStride, b] = tmpArrayForDithering[i  + pseudoStride, b] + quant_error * 5.0f / 16.0f;
                    tmpArrayForDithering[i + 3 + pseudoStride, b] = tmpArrayForDithering[i + 3 + pseudoStride, b] + quant_error * 1.0f / 16.0f;
                    
                    // pixels[x + 1][y    ] := pixels[x + 1][y    ] + quant_error × 7 / 16
                    //pixels[x - 1][y + 1] := pixels[x - 1][y + 1] + quant_error × 3 / 16
                    //pixels[x    ][y + 1] := pixels[x    ][y + 1] + quant_error × 5 / 16
                    //pixels[x + 1][y + 1] := pixels[x + 1][y + 1] + quant_error × 1 / 16
                    
                    //tmpArrayForDithering[i, b];
                }

            }*/

            return pixelBitsFloatArrayToByteArray(ref tmpArrayForDithering);
        }

        private void btnSaveResult_Click(object sender, RoutedEventArgs e)
        {
            resultBitmap.Save(Helpers.GetUnusedFilename("result.png"));
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double ratio = ratioSlider.Value;
            setRatio(ratio);
        }

        private void setRatio(double ratio)
        {

            double inverseRatio = 1.0 - ratio;

            imagesToMix[1].ratio = (float)ratio;
            imagesToMix[0].ratio = (float)inverseRatio;
        }

        private void animateBtn_Click(object sender, RoutedEventArgs e)
        {
            DoAnimate();
        }

        private async void DoAnimate()
        {
            double ratioStepSize = stepSlider.Value;
            double fps = fpsSlider.Value;
            double gamma = ratioGammaSlider.Value;
            await Task.Run(()=> { 
                int ratioStepCount =(int)(1+ Math.Ceiling(1.0 / ratioStepSize)); // 1+ because 0 is also a value.
            
                double[] ratiosToDo = new double[ratioStepCount];
                for(int i = 0; i < ratioStepCount; i++)
                {
                    double linearValue = Math.Min(1.0, i * ratioStepSize);
                    double distanceFromCenter = Math.Abs(linearValue - 0.5);
                    double sign = Math.Sign(linearValue - 0.5);

                    double distanceFromCenterWithGamma = Math.Pow(distanceFromCenter*2,1/gamma)/2;
                    double valueWithGamma = 0.5 + distanceFromCenterWithGamma * sign;

                    ratiosToDo[i] = valueWithGamma;
                }

                AviWriter writer = new AviWriter(Helpers.GetUnusedFilename("result.avi")) {
                    FramesPerSecond = (decimal)fps,
                    // Emitting AVI v1 index in addition to OpenDML index (AVI v2)
                    // improves compatibility with some software, including 
                    // standard Windows programs like Media Player and File Explorer
                    EmitIndex1 = true
                };
            
                IAviVideoStream stream= writer.AddEncodingVideoStream(new SharpAvi.Codecs.UncompressedVideoEncoder(imagesToMix[0].srcImage.Width, imagesToMix[0].srcImage.Height), width:imagesToMix[0].srcImage.Width,height: imagesToMix[0].srcImage.Height);
            

                int pixelCount = imagesToMix[0].srcImage.Width * imagesToMix[0].srcImage.Height * 4;
                byte[] buffer = new byte[pixelCount];

                int index = 0;
                foreach (double ratio in ratiosToDo)
                {
                    setRatio(ratio);
                    using (Bitmap bitmap = getMixedImage())
                    {
                        var bits = bitmap.LockBits(new Rectangle(0, 0, stream.Width, stream.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                        Marshal.Copy(bits.Scan0, buffer, 0, buffer.Length);
                        bitmap.UnlockBits(bits);
                        stream.WriteFrame(true, buffer, 0, buffer.Length);
                        //stream.WriteFrame(true, image, 0, pixelCount);

                    }

                    Dispatcher.Invoke(()=> {
                        animationStatusTxt.Text = "Rendering video. " + (++index) + " frames out of " + ratioStepCount.ToString();

                    });
                    
                }


                writer.Close();
                //resultBitmap = getMixedImage();
            });
            animationStatusTxt.Text = "Video done";
        }

    }
}
