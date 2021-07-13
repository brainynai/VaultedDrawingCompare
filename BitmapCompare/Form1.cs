using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using EPDM.Interop.epdm;
using EPDM.Interop.EPDMResultCode;
using SolidWorks.Interop.sldworks;

namespace BitmapCompare
{
    public partial class Form1 : Form
    {
        private string selectedFilePath { get; set; }
        private string[] versions { get; set; }
        private string[] laterVersions { get; set; }
        private string imgPathFromVersion { get; set; }
        private string imgPathToVersion { get; set; }
        private Bitmap diffBmp { get; set; }

        private IEdmVault5 myVault = null;
        private string vaultName = "PROM_TEST";
        private string tempFolder1 = null;
        private string tempFolder2 = null;
        private string fromFileHashStr = null;
        private string toFileHashStr = null;
        private Dictionary<string, string> fromBmps = new Dictionary<string, string>();
        private Dictionary<string, string> toBmps = new Dictionary<string, string>();

        private HashSet<(int x, int y)> diffPixels = new HashSet<(int x, int y)>();
        private HashSet<(int x, int y)> additionPixels = new HashSet<(int x, int y)>();
        private HashSet<(int x, int y)> subtractionPixels = new HashSet<(int x, int y)>();

        public Form1()
        {
            InitializeComponent();
        }

        public void Form1_Load(System.Object sender, System.EventArgs e)
        {
            //Declare and create an instance of IEdmVault5
            myVault = new EdmVault5();
            try
            {
                myVault.LoginAuto(vaultName, this.Handle.ToInt32());
            }
            catch(Exception)
            { 
            }

            if (!myVault.IsLoggedIn)
                MessageBox.Show($"Vault login failed.. Does {vaultName} exist?");
        }


        //File select
        private void btnSelectFile_Click(object sender, EventArgs e)
        {
            EdmStrLst5 selectedFileList =
                myVault.BrowseForFile(this.Handle.ToInt32(),
                (int)(EdmBrowseFlag.EdmBws_ForOpen | EdmBrowseFlag.EdmBws_PermitVaultFiles),
                "Drawing Files (*.SLDDRW) |*.SLDDRW||");

            //DialogResult result = openFileDialog1.ShowDialog();
            if (selectedFileList != null)
            {
                comboFromVersion.Items.Clear();
                IEdmPos5 pos = selectedFileList.GetHeadPosition();
                selectedFilePath = selectedFileList.GetNext(pos);
                textBox1.Text = selectedFilePath;
                versions = DataCollection.getVersions(ref myVault, selectedFilePath);
                comboFromVersion.Items.AddRange(versions);
                comboFromVersion.Enabled = true;
                comboFromVersion.SelectedIndex = 0;
                comboToVersion.Enabled = false;
                comboToVersion.Items.Clear();
                comboFromSheet.Enabled = false;
                comboFromSheet.Items.Clear();
                comboToSheet.Enabled = false;
                comboToSheet.Items.Clear();

                pictureBox1.Image = null;
                pictureBox2.Image = null;
                pictureBox3.Image = null;

                diffPixels.Clear();
                additionPixels.Clear();
                subtractionPixels.Clear();

                checkBox1.Checked = true;
                checkBox1.Enabled = false;
                checkBox2.Checked = true;
                checkBox2.Enabled = false;

                compareStatusLabel.Text = "Please complete drawing/version selection.";
            }

        }

        private void comboFromVersion_SelectedValueChanged(object sender, EventArgs e)
        {
            //Make sure version comes in as valid int.. 

            string selection = comboFromVersion.Text;

            var laterQuery = from ver in versions
                             where int.Parse(ver) > int.Parse(selection)
                             select ver;

            laterVersions = laterQuery.ToArray();

            comboToVersion.Items.Clear();

            if (laterVersions.Length > 0)
            {
                comboToVersion.Items.AddRange(laterVersions);
                comboToVersion.Enabled = true;
                comboToVersion.SelectedIndex = 0;
            }
            else
            {
                comboToVersion.Enabled = false;
                comboToVersion.Text = "No Later Version";
                btnLoadDrawings.Enabled = false;
            }

        }

        private void comboToVersion_SelectedValueChanged(object sender, EventArgs e)
        {
            btnLoadDrawings.Enabled = true;
        }

        private void btnLoadDrawings_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;

            int v1 = int.Parse(comboFromVersion.Text);
            int v2 = int.Parse(comboToVersion.Text);

            /*
             * 
             * TODO: CROP BMP TO THE DRAWING BORDER... ELIMINATE RANDOM BITMAP SHIFT? 
             * 
             * Consider saving to PDF as intermediate step? 
             * 
             * */

            IEdmEnumeratorVersion5 enumVersion = (IEdmEnumeratorVersion5)myVault.GetFileFromPath(selectedFilePath, out IEdmFolder5 theFolder);
            IEdmVersion5 version1 = enumVersion.GetVersion(v1);
            IEdmVersion5 version2 = enumVersion.GetVersion(v2); 

            IEdmFile5 theFile = myVault.GetFileFromPath(selectedFilePath, out theFolder);

            compareStatusLabel.Text = "Getting first version...";
            IEdmBatchGet batchGetter = (IEdmBatchGet)((IEdmVault7)myVault).CreateUtility(EdmUtility.EdmUtil_BatchGet);
            batchGetter.AddSelectionEx((EdmVault5)myVault, theFile.ID, theFolder.ID, v1);
            batchGetter.CreateTree(this.Handle.ToInt32(), (int)EdmGetCmdFlags.Egcf_AsBuilt);
            if (!DataCollection.anyFilesCheckedOut(ref myVault, batchGetter.GetFileList((int)EdmGetFileListFlag.Egflf_GetRetrieved), out string lockedFile))
            {
                batchGetter.GetFiles(this.Handle.ToInt32(), null);
            }
            else
            {
                MessageBox.Show($"Getting version {v1} failed. At least the following is checked out:\n{lockedFile}");
                this.Cursor = Cursors.Default;
                compareStatusLabel.Text = "Failed to get first version..";
                return;
            }
            

            tempFolder1 = Path.GetTempFileName();
            File.Delete(tempFolder1);
            Directory.CreateDirectory(tempFolder1);
            tempFolder1 += "\\";
            string tempFileName1 = "OLDERVERSION.SLDDRW";

            tempFolder2 = Path.GetTempFileName();
            File.Delete(tempFolder2);
            Directory.CreateDirectory(tempFolder2);
            tempFolder2 += "\\";
            string tempFileName2 = "NEWERVERSION.SLDDRW";


            /*
             * TODO: Eliminate copying of drawing file because we're getting the file versions in the vault now
             */

            try
            {
                version1.GetFileCopy(this.Handle.ToInt32(), tempFolder1, 0, tempFileName1);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                switch (ex.ErrorCode)
                {
                    case (int)EdmResultErrorCodes_e.E_EDM_FILE_NOT_FOUND:
                        MessageBox.Show("File not found");
                        return;
                    case (int)EdmResultErrorCodes_e.E_EDM_PERMISSION_DENIED:
                        MessageBox.Show("Permission denied");
                        return;
                    default:
                        MessageBox.Show(ex.Message);
                        return;
                }
            }

            var progId = "SldWorks.Application";
            var progType = System.Type.GetTypeFromProgID(progId);
            var app = System.Activator.CreateInstance(progType) as SolidWorks.Interop.sldworks.ISldWorks;
            app.Visible = true;

            int errs = 0;
            int warns = 0;
            var model = app.OpenDoc6(Path.Combine(tempFolder1, tempFileName1), 3, 16, "", ref errs, warns);
            var modelExt = (ModelDocExtension)model.Extension;
            var dwg = (DrawingDoc)model;
            //model.Visible = true;
            app.ActivateDoc3(tempFileName1, true, 1, errs);

            string[] sheetNames = (string[])dwg.GetSheetNames();

            // TODO: Make reload conditional on whether or not the version has changed... 
            fromBmps = new Dictionary<string, string>();

            (int picWidth, int picHeight) PicDims = (0, 0);

            comboFromSheet.Items.Clear();
            foreach (var sheetName in sheetNames)
            {
                comboFromSheet.Items.Add(sheetName);
                fromBmps.Add(sheetName, Path.Combine(tempFolder1, sheetName + ".bmp"));
                dwg.ActivateSheet(sheetName);
                //model.ViewZoomtofit2();
                modelExt.ViewZoomToSheet();
                model.SaveBMP(Path.Combine(tempFolder1, sheetName + ".bmp"), PicDims.picWidth, PicDims.picHeight);
                
                //Establish size
                if (PicDims.picWidth == 0)
                {
                    using (var img = Bitmap.FromFile(fromBmps[sheetName]))
                    {
                        PicDims.picWidth = img.Width;
                        PicDims.picHeight = img.Height;
                    }
                }
            }
            imgPathFromVersion = fromBmps[fromBmps.Keys.ElementAt(0)];
            pictureBox1.ImageLocation = imgPathFromVersion;
            comboFromSheet.Enabled = true;
            comboFromSheet.SelectedIndex = 0;
            app.CloseAllDocuments(true);


            compareStatusLabel.Text = "Getting second version...";
            batchGetter = (IEdmBatchGet)((IEdmVault7)myVault).CreateUtility(EdmUtility.EdmUtil_BatchGet);
            batchGetter.AddSelectionEx((EdmVault5)myVault, theFile.ID, theFolder.ID, v2);
            batchGetter.CreateTree(this.Handle.ToInt32(), (int)EdmGetCmdFlags.Egcf_AsBuilt);
            if (!DataCollection.anyFilesCheckedOut(ref myVault, batchGetter.GetFileList((int)EdmGetFileListFlag.Egflf_GetRetrieved), out lockedFile))
            {
                batchGetter.GetFiles(this.Handle.ToInt32(), null);
            }
            else
            {
                MessageBox.Show($"Getting version {v2} failed. At least the following is checked out:\n{lockedFile}");
                this.Cursor = Cursors.Default;
                compareStatusLabel.Text = "Failed to get second version!";
                return;
            }

            try
            {
                version2.GetFileCopy(this.Handle.ToInt32(), tempFolder2, 0, tempFileName2);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                switch (ex.ErrorCode)
                {
                    case (int)EdmResultErrorCodes_e.E_EDM_FILE_NOT_FOUND:
                        MessageBox.Show("File not found");
                        return;
                    case (int)EdmResultErrorCodes_e.E_EDM_PERMISSION_DENIED:
                        MessageBox.Show("Permission denied");
                        return;
                    default:
                        MessageBox.Show(ex.Message);
                        return;
                }
            }

            errs = 0;
            warns = 0;
            model = app.OpenDoc6(Path.Combine(tempFolder2, tempFileName2), 3, 16, "", ref errs, warns);
            modelExt = (ModelDocExtension)model.Extension;
            dwg = (DrawingDoc)model;
            app.ActivateDoc3(tempFileName2, true, 1, errs);
            sheetNames = (string[])dwg.GetSheetNames();

            toBmps = new Dictionary<string, string>();
            comboToSheet.Items.Clear();
            foreach (var sheetName in sheetNames)
            {
                comboToSheet.Items.Add(sheetName);
                toBmps.Add(sheetName, Path.Combine(tempFolder2, sheetName + ".bmp"));
                dwg.ActivateSheet(sheetName);
                modelExt.ViewZoomToSheet();
                model.SaveBMP(Path.Combine(tempFolder2, sheetName + ".bmp"), PicDims.picWidth, PicDims.picHeight);
                
            }
            imgPathToVersion = toBmps[fromBmps.Keys.ElementAt(0)];
            pictureBox2.ImageLocation = imgPathToVersion;
            comboToSheet.Enabled = true;
            comboToSheet.SelectedIndex = 0;
            app.CloseAllDocuments(true);


            app.ExitApp();

            this.Cursor = Cursors.Default;

            btnLoadDrawings.Enabled = false;
            btnCompare.Enabled = true;

            //Restore latest version of files
            int maxVersion = (Array.ConvertAll(versions, int.Parse)).Max();
            batchGetter = (IEdmBatchGet)((IEdmVault7)myVault).CreateUtility(EdmUtility.EdmUtil_BatchGet);
            batchGetter.AddSelectionEx((EdmVault5)myVault, theFile.ID, theFolder.ID, maxVersion);
            batchGetter.CreateTree(this.Handle.ToInt32(), (int)EdmGetCmdFlags.Egcf_AsBuilt);
            batchGetter.GetFiles(this.Handle.ToInt32(), null);

            compareStatusLabel.Text = "Versions retrieved. Click compare to start comparison.";

        }

        private void btnCompare_Click(object sender, EventArgs e)
        {
            int bgClr = Color.White.ToArgb();

            compareStatusLabel.Text = "Running comparison...";
            this.Cursor = Cursors.WaitCursor;
            btnCompare.Enabled = false;

            Bitmap bmp1 = (Bitmap)Bitmap.FromFile(imgPathFromVersion);
            Bitmap bmp2 = (Bitmap)Bitmap.FromFile(imgPathToVersion);

            diffBmp = new Bitmap(bmp1.Width, bmp1.Height);

            for (int y = 0; y < diffBmp.Height; y++)
            {
                for (int x = 0; x < diffBmp.Width; x++)
                {
                    int oldColor = bmp1.GetPixel(x, y).ToArgb();
                    int newColor = bmp2.GetPixel(x, y).ToArgb();

                    
                    if (oldColor != newColor)
                    {
                        if (oldColor == bgClr && newColor != bgClr)
                        {
                            additionPixels.Add((x, y));
                            diffBmp.SetPixel(x, y, Color.Green);
                        }
                        else if (oldColor != bgClr && newColor == bgClr)
                        {
                            subtractionPixels.Add((x, y));
                            diffBmp.SetPixel(x, y, Color.Red);
                        }
                        else
                        {
                            diffPixels.Add((x, y));
                            diffBmp.SetPixel(x, y, Color.White);
                        }
                    }
                    else
                    {
                        diffBmp.SetPixel(x, y, Color.Black);
                    }
                }
            }

            pictureBox3.Image = diffBmp;
            checkBox1.Enabled = true;
            checkBox2.Enabled = true;
                        
            //Need to dispose bitmaps to release file handle
            bmp1.Dispose();
            bmp2.Dispose();

            this.Cursor = Cursors.Default;
            compareStatusLabel.Text = "Comparison complete!";
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (tempFolder1 != null)
            {
                try
                {
                    Directory.Delete(tempFolder1, true);
                    tempFolder1 = null;
                }
                catch (System.IO.IOException)
                {
                    MessageBox.Show($"Failed to delete temp files from\n{tempFolder1}");
                }
            }

            if (tempFolder2 != null)
            {
                try
                {
                    Directory.Delete(tempFolder2, true);
                    tempFolder2 = null;
                }
                catch (System.IO.IOException)
                {
                    MessageBox.Show($"Failed to delete temp files from\n{tempFolder2}");
                }

            }
        }

        private void comboFromSheet_SelectedValueChanged(object sender, EventArgs e)
        {
            imgPathFromVersion = Path.Combine(tempFolder1, comboFromSheet.Text + ".bmp");
            pictureBox1.ImageLocation = imgPathFromVersion;

            if (DataCollection.fileChanged(imgPathFromVersion, ref fromFileHashStr))
            {
                btnCompare.Enabled = true;
            }

        }

        private void comboToSheet_SelectedValueChanged(object sender, EventArgs e)
        {
            imgPathToVersion = Path.Combine(tempFolder2, comboToSheet.Text + ".bmp");
            pictureBox2.ImageLocation = imgPathToVersion;

            if (DataCollection.fileChanged(imgPathToVersion, ref toFileHashStr))
            {
                btnCompare.Enabled = true;
            }
        }

        //TODO: Rename this.. not handling event from tablelayout anymore
        private void tableLayoutPanel1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            
            //MessageBox.Show($"Double clicked\n({e.X},{e.Y})");
            if(pictureBox3.SizeMode == PictureBoxSizeMode.Zoom)
            {
                //int newX = (int)((e.X - pictureBox3.ClientRectangle.X)*1.0/panel1.Width * 1000);
                //int newY = (int)((e.Y - pictureBox3.ClientRectangle.Y)*1.0/panel1.Height * 1000);

                double newX = e.X/1.0/pictureBox3.Width;
                double newY = e.Y/1.0/pictureBox3.Height;

                pictureBox3.SizeMode = PictureBoxSizeMode.AutoSize;
                pictureBox3.Dock = DockStyle.None;


                panel1.AutoScrollMinSize = new Size(pictureBox3.Width,pictureBox3.Height ); //new Size(1000, 1000); 

                //panel1.AutoScrollPosition = new Point((int)(newX* pictureBox3.Width), (int)(newY*pictureBox3.Height));

                double offsetH = panel1.HorizontalScroll.LargeChange / 2.0 ;
                double offsetV = panel1.VerticalScroll.LargeChange / 2.0;

                int newHVal = (int)(newX * (panel1.HorizontalScroll.Maximum - panel1.HorizontalScroll.Minimum) - offsetH);
                int newVVal = (int)(newY * (panel1.VerticalScroll.Maximum - panel1.VerticalScroll.Minimum) - offsetV);

                if (newHVal > panel1.HorizontalScroll.Maximum)
                {
                    newHVal = panel1.HorizontalScroll.Maximum;
                }
                else if (newHVal < panel1.HorizontalScroll.Minimum)
                {
                    newHVal = panel1.HorizontalScroll.Minimum;
                }

                panel1.HorizontalScroll.Value = newHVal;
                

                if (newVVal > panel1.VerticalScroll.Maximum)
                {
                    newVVal = panel1.VerticalScroll.Maximum;
                }
                else if (newVVal < panel1.VerticalScroll.Minimum)
                {
                    newVVal = panel1.VerticalScroll.Minimum;
                }


                panel1.VerticalScroll.Value = newVVal;
                

                pictureBox1.SizeMode = PictureBoxSizeMode.AutoSize;
                pictureBox1.Dock = DockStyle.None;
                panel2.AutoScrollMinSize = new Size(pictureBox3.Width, pictureBox3.Height);
                panel2.HorizontalScroll.Value = newHVal;
                panel2.VerticalScroll.Value = newVVal;

                pictureBox2.SizeMode = PictureBoxSizeMode.AutoSize;
                pictureBox2.Dock = DockStyle.None;
                panel3.AutoScrollMinSize = new Size(pictureBox3.Width, pictureBox3.Height);
                panel3.HorizontalScroll.Value = newHVal;
                panel3.VerticalScroll.Value = newVVal;

            }
            else
            {
                panel1.AutoScrollPosition = new Point(0, 0);
                pictureBox3.SizeMode = PictureBoxSizeMode.Zoom;
                pictureBox3.Dock = DockStyle.Fill;
                panel1.AutoScrollMinSize = new Size(0, 0);

                panel2.AutoScrollPosition = new Point(0, 0);
                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                pictureBox1.Dock = DockStyle.Fill;
                panel2.AutoScrollMinSize = new Size(0, 0);

                panel3.AutoScrollPosition = new Point(0, 0);
                pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
                pictureBox2.Dock = DockStyle.Fill;
                panel3.AutoScrollMinSize = new Size(0, 0);
            }
            
        }


        private void panel1_Scroll(object sender, ScrollEventArgs e)
        {
            panel2.HorizontalScroll.Value = panel1.HorizontalScroll.Value;
            panel2.VerticalScroll.Value = panel1.VerticalScroll.Value;

            panel3.HorizontalScroll.Value = panel1.HorizontalScroll.Value;
            panel3.VerticalScroll.Value = panel1.VerticalScroll.Value;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                //Add in additions
                foreach (var pixel in additionPixels)
                {
                    diffBmp.SetPixel(pixel.x, pixel.y, Color.Green);
                }
            }
            else
            {
                //Take out additions
                foreach (var pixel in additionPixels)
                {
                    diffBmp.SetPixel(pixel.x, pixel.y, Color.Black);
                }
            }

            pictureBox3.Image = diffBmp;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                //Add in subractions
                foreach (var pixel in subtractionPixels)
                {
                    diffBmp.SetPixel(pixel.x, pixel.y, Color.Red);
                }
            }
            else
            {
                //Take out subtractions
                foreach (var pixel in subtractionPixels)
                {
                    diffBmp.SetPixel(pixel.x, pixel.y, Color.Black);
                }
            }

            pictureBox3.Image = diffBmp;
        }

 

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox1 a = new AboutBox1();
            a.ShowDialog();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(diffBmp == null)
            {
                MessageBox.Show("You need to have a compare picture before you can save it..");
                return;
            }

            Stream myStream;

            try
            {
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    if ((myStream = saveFileDialog1.OpenFile()) != null)
                    {
                        diffBmp.Save(myStream, ImageFormat.Bmp);
                        myStream.Close();
                    }
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Save failed for some reason.");
            }
            
        }

        /*TODO

        Config file? Ability for user to save settings?

        Watermark bmp? :P

        Crop oversize bmp / attempt to eliminate random tiny shifts in bmp

        Double check that temp files are getting deleted in all scenarios.. change version, change source file

        */
    }
}
