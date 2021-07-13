using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EPDM.Interop.epdm;
using System.Security.Cryptography;
using System.IO;

namespace BitmapCompare
{
    public class DataCollection
    {
        internal static string[] getVersions(ref IEdmVault5 myVault, string filepath)
        {
            IEdmEnumeratorVersion5 enumVersion = (IEdmEnumeratorVersion5)myVault.GetFileFromPath(filepath, out IEdmFolder5 theFolder);
            IEdmPos5 pos = enumVersion.GetFirstVersionPosition();

            List<string> versionList = new List<string>();

            while (!pos.IsNull)
            {
                versionList.Add(enumVersion.GetNextVersion(pos).VersionNo.ToString());
            }

            versionList.Sort();

            return versionList.ToArray();
        }

        internal static bool fileChanged(string filepath, ref string oldHash)
        {
            string thisHash;
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filepath))
                {
                    var hash = md5.ComputeHash(stream);
                    thisHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }

            if (thisHash.Equals(oldHash))
            {
                return false;
            }
            else
            {
                oldHash = thisHash;
                return true;
            }
        }

        internal static bool anyFilesCheckedOut(ref IEdmVault5 myVault, IEdmSelectionList5 selList, out string lockedFile)
        {
            IEdmSelectionList6 selList6 = (IEdmSelectionList6)selList;
            IEdmPos5 pos = selList6.GetHeadPosition();
            EdmSelectionObject thisObj = default(EdmSelectionObject);
            IEdmFile5 theFile = default(IEdmFile5);
            

            while (!pos.IsNull)
            {
                selList6.GetNext2(pos, out thisObj);
                theFile = myVault.GetFileFromPath(thisObj.mbsPath, out IEdmFolder5 theFolder);

                if (theFile.IsLocked)
                {
                    lockedFile = thisObj.mbsPath;
                    return true;
                }
            }

            lockedFile = null;
            return false; //No files are currently checked out
        }

    }
}
