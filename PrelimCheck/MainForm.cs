using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using System.Reflection;
using System.Net;
using System.IO;


namespace PrelimCheck
{
    public partial class MainForm : Form
    {
        CredentialCache myCredentialCache;

        public MainForm()
        {
            InitializeComponent();

            Assembly asm = Assembly.GetExecutingAssembly();
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            DateTime compileDate = new DateTime(v.Build * TimeSpan.TicksPerDay + v.Revision * TimeSpan.TicksPerSecond * 2).AddYears(1999);
            if (TimeZone.IsDaylightSavingTime(compileDate, TimeZone.CurrentTimeZone.GetDaylightChanges(compileDate.Year)))
            {
                compileDate = compileDate.AddHours(1);
            }
            this.labelVersion.Text = String.Format("Build {0}", compileDate);

            ServicePointManager.Expect100Continue = false;
            // Ignore Certificate validation failures (aka untrusted certificate + certificate chains)
            ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);

            myCredentialCache = new CredentialCache();

            DateTime dt = DateTime.Now;
            dt=dt.Date.AddDays(-1);
            dt = dt.Date + new TimeSpan(18, 0, 0);
            dateTimePickerStart.Value = dt;
            dt = dt.Date.AddDays(1);
            dt = dt.Date + new TimeSpan(8, 0, 0);
            dateTimePickerEnd.Value = dt;

        }

        private void btnRetrieve_Click(object sender, EventArgs e)
        {
            string url = "http://lacsynapse/SynapseScripts/fujirds.asp";
            if (rbKeck.Checked) url = "https://external.synapse.uscuh.com/SynapseScripts/fujirds.asp";

            Uri uriFujiRDS = new Uri(url);

            string dateStart = dateTimePickerStart.Value.ToString("yyyy-MM-dd HH:mm:ss");
            string dateEnd = dateTimePickerEnd.Value.ToString("yyyy-MM-dd HH:mm:ss");
            string query = String.Format(@"select * from storage s,document d,study_document sd,study st,patient p,procedure_info pi 
              where d.id = sd.document_uid 
                and s.id=d.storage_uid 
                and st.id=sd.study_uid 
                and p.id=st.patient_uid 
                and pi.id=st.procedure_info_uid 
                and d.name='Notes' 
                and d.creation_timedate between to_date('{0}','YYYY-MM-DD HH24:MI:SS') and to_date('{1}','YYYY-MM-DD HH24:MI:SS')", dateStart, dateEnd);

            ADODB.Recordset rs = new ADODB.Recordset();
            byte[] result = retrieveRDS(uriFujiRDS, query);

            if (result == null) return;
            string tempfile = Path.GetTempFileName();
            ByteArrayToFile(tempfile, result);
            rs.Open(tempfile, "Provider=MSPersist", ADODB.CursorTypeEnum.adOpenStatic, ADODB.LockTypeEnum.adLockReadOnly, 0);

            string notefile = Path.GetTempFileName();
            WebClient client = new WebClient();
            client.Credentials = myCredentialCache;

            DataTable dt = new DataTable();
            dt.Columns.Add("Accession", typeof(string));
            dt.Columns.Add("MRN", typeof(string));
            dt.Columns.Add("Procedure", typeof(string));
            dt.Columns.Add("Name", typeof(string));
            dt.Columns.Add("Time", typeof(string));
            dt.Columns.Add("Note", typeof(string));

            while (!rs.EOF)
            {
                string http_url = rs.Fields["http_url"].Value.ToString();
                string https_url = rs.Fields["https_url"].Value.ToString();
                string accnum = rs.Fields["ris_study_euid"].Value.ToString();
                string proc = rs.Fields["description"].Value.ToString();
                string proc_code = rs.Fields["code"].Value.ToString();
                string filename = rs.Fields["filename"].Value.ToString();
                string mrn = rs.Fields["internal_euid"].Value.ToString();
                string lastname=rs.Fields["last_name"].Value.ToString();
                string firstname=rs.Fields["first_name"].Value.ToString();
                string middlename=rs.Fields["middle_name"].Value.ToString();
                string creation_time=rs.Fields["creation_timedate"].Value.ToString();

                if (rbCounty.Checked)
                {
                    filename = http_url + filename;
                }
                else
                {
                    filename = https_url + filename;
                }
                client.DownloadFile(filename, notefile);
                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.Load(notefile);
                string header=doc.DocumentNode.SelectSingleNode("//div[@class=\"clsNoteHeader\"]").InnerText;
                header = header.Replace("&nbsp;", " ").Trim();
                string note = header+"\r\n\r\n";

                string content = doc.DocumentNode.SelectSingleNode("//div[@class=\"clsNoteData\"]").InnerText;
                content = content.Replace("&nbsp;", " ").Trim();
                note += content;
                string name=(lastname+", "+firstname+" "+middlename).Trim();

                if (note.Contains(textBoxFilter.Text))
                {
                    dt.Rows.Add(accnum, mrn, proc, name, creation_time, note);
                }

                rs.MoveNext();
            }
            dgv_Results.DataSource = dt;
            rs.Close();
            File.Delete(tempfile);
        }


        public bool ByteArrayToFile(string _FileName, byte[] _ByteArray)
        {
            try
            {
                // Open file for reading
                System.IO.FileStream _FileStream = new System.IO.FileStream(_FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write);

                // Writes a block of bytes to this stream using data from a byte array.
                _FileStream.Write(_ByteArray, 0, _ByteArray.Length);

                // close file stream
                _FileStream.Close();

                return true;
            }
            catch (Exception _Exception)
            {
                // Error
                Console.WriteLine("Exception caught in process: {0}", _Exception.ToString());
            }

            // error occured, return false
            return false;
        }


        private byte[] retrieveRDS(Uri uriRDS, string query)
        {
            string queryEsc = query.Replace(" ", "%20");
            queryEsc = "cmd=" + queryEsc + "&";
            byte[] buffer = new byte[65536];
            byte[] postBytes = Encoding.ASCII.GetBytes(queryEsc);

            Uri baseURI = new Uri(uriRDS.GetLeftPart(UriPartial.Authority));

            while (true)
            {
                try
                {
                    HttpWebRequest myWebRequest = (HttpWebRequest)WebRequest.Create(uriRDS);
                    myWebRequest.Method = "POST";


                    if (myCredentialCache.GetCredential(baseURI, "NTLM") == null)
                    {
                        myCredentialCache.Add(baseURI, "NTLM", CredentialCache.DefaultNetworkCredentials);
                    }
                    myWebRequest.Credentials = myCredentialCache;

                    myWebRequest.ContentType = "application/x-www-form-urlencoded";
                    myWebRequest.ContentLength = postBytes.Length;

                    myWebRequest.Timeout = 300 * 1000;

                    Stream requestStream = myWebRequest.GetRequestStream();

                    // now send it
                    requestStream.Write(postBytes, 0, postBytes.Length);
                    requestStream.Close();

                    WebResponse webResponse = myWebRequest.GetResponse();
                    using (Stream input = webResponse.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (true)
                        {
                            int read = input.Read(buffer, 0, buffer.Length);
                            if (read <= 0)
                                return ms.ToArray();
                            ms.Write(buffer, 0, read);
                        }
                    }
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.ProtocolError)
                    {
                        HttpWebResponse httpResponse = (HttpWebResponse)e.Response;
                        int statusCode = (int)httpResponse.StatusCode;
                        //textBoxLine(statusCode + " - " + httpResponse.StatusCode);

                        if (statusCode == 401)
                        {
                            //textBoxLine(e.Response.Headers["WWW-Authenticate"]);
                            Authentication authDialog = new Authentication();
                            authDialog.Reset("Connect to " + baseURI);

                            MessageBoxHelper.PrepToCenterMessageBoxOnForm(this);
                            DialogResult result = authDialog.ShowDialog(this);
                            if (result == DialogResult.OK)
                            {
                                string username = authDialog.username;
                                string domain = "";
                                if (authDialog.username.Contains(@"\"))
                                {
                                    domain = authDialog.username.Split(new char[] { '\\' })[0];
                                    username = authDialog.username.Split(new char[] { '\\' })[1];
                                }
                                myCredentialCache.Remove(baseURI, "Basic");
                                myCredentialCache.Remove(baseURI, "NTLM");
                                myCredentialCache.Add(baseURI, "Basic", new NetworkCredential(username, authDialog.password, domain));
                                myCredentialCache.Add(baseURI, "NTLM", new NetworkCredential(username, authDialog.password, domain));
                                continue;
                            }
                        }
                    }
                    else
                    {
                        //textBoxLine(e.Message + "\r\n" + e.StackTrace);
                    }
                    return null;
                }

            }

        }

        private void dgv_Results_SelectionChanged(object sender, EventArgs e)
        {
            if (dgv_Results.SelectedRows.Count > 0)
            {
                textBoxNote.Text = dgv_Results.SelectedRows[0].Cells["Note"].Value.ToString();
            }
        }



    }
}
