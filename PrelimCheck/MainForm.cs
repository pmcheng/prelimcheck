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
using System.Web;
using LumenWorks.Framework.IO.Csv;

namespace PrelimCheck
{
    public partial class MainForm : Form
    {
        CredentialCache myCredentialCache;
        DataTable dt;

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

            DateTime dtime = DateTime.Now;
            dtime = dtime.Date.AddDays(-1);
            dtime = dtime.Date + new TimeSpan(18, 0, 0);
            dateTimePickerStart.Value = dtime;
            dtime = dtime.Date.AddDays(1);
            //dtime = dtime.Date + new TimeSpan(8, 0, 0);
            //dateTimePickerEnd.Value = dtime;
            cbDuration.SelectedIndex = 2;

            saveFileDialog.InitialDirectory = Application.StartupPath;
            openFileDialog.InitialDirectory = Application.StartupPath;

            btnSave.Enabled = false;

            typeof(DataGridView).InvokeMember(
               "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null,
                dgv_Results,
                new object[] { true });


        }

        private void btnRetrieve_Click(object sender, EventArgs e)
        {
            btnRetrieve.Enabled = false;
            btnSave.Enabled = false;
            btnLoad.Enabled = false;

            string url = "http://lacsynapse/SynapseScripts/fujirds.asp";
            if (rbKeck.Checked) url = "http://synapse.uscuh.com/SynapseScripts/fujirds.asp";
            if (rbKeckRemote.Checked) url = "https://external.synapse.uscuh.com/SynapseScripts/fujirds.asp";

            Uri uriFujiRDS = new Uri(url);

            string query = "select * from storage where rownum=1"; // test query
            byte[] result = retrieveRDS(uriFujiRDS, query);
            if (result != null)
            {
                BackgroundParameter bObj = new BackgroundParameter();
                bObj.url = url;
                bObj.duration = cbDuration.Text;
                backgroundWorker.RunWorkerAsync(bObj);
            }
            else
            {
                btnRetrieve.Enabled = true;
                btnLoad.Enabled = true;
            }


        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                BackgroundParameter bObj = e.Argument as BackgroundParameter;
                string url = bObj.url;
                Uri uriFujiRDS = new Uri(url);

                ProgressObject pObj = new ProgressObject();
                backgroundWorker.ReportProgress(0, pObj);

                DateTime dtime = dateTimePickerStart.Value;
                string dateStart = dtime.ToString("yyyy-MM-dd HH:mm:ss");

                string dateEnd = dtime.AddHours(double.Parse(bObj.duration)).ToString("yyyy-MM-dd HH:mm:ss");
                //dateTimePickerEnd.Value.ToString("yyyy-MM-dd HH:mm:ss");
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

                dt = new DataTable();

                dt.Columns.Add("Name", typeof(string));
                dt.Columns.Add("MRN", typeof(string));
                dt.Columns.Add("Accession", typeof(string));
                dt.Columns.Add("Procedure", typeof(string));
                dt.Columns.Add("Study Time", typeof(string));
                dt.Columns.Add("Note Time", typeof(string));
                dt.Columns.Add("Note", typeof(string));
                dt.Columns.Add("Reports", typeof(string));

                DateTime lastprogress = DateTime.Now;

                while (!rs.EOF)
                {
                    string http_url = rs.Fields["http_url"].Value.ToString();
                    string https_url = rs.Fields["https_url"].Value.ToString();
                    string accnum = rs.Fields["ris_study_euid"].Value.ToString();
                    string proc = rs.Fields["description"].Value.ToString();
                    string proc_code = rs.Fields["code"].Value.ToString();
                    string filename = rs.Fields["filename"].Value.ToString();
                    string mrn = rs.Fields["internal_euid"].Value.ToString();
                    string lastname = rs.Fields["last_name"].Value.ToString();
                    string firstname = rs.Fields["first_name"].Value.ToString();
                    string middlename = rs.Fields["middle_name"].Value.ToString();
                    string study_time = ((DateTime)rs.Fields["study_timedate"].Value).ToString("s").Replace('T', ' ');
                    string creation_time = ((DateTime)rs.Fields["creation_timedate"].Value).ToString("s").Replace('T', ' ');


                    if (url.StartsWith("https"))
                    {
                        filename = https_url + filename;
                    }
                    else
                    {
                        filename = http_url + filename;
                    }

                    string note = "";
                    try
                    {
                        client.DownloadFile(filename, notefile);
                        note = parseNote(notefile);
                    }
                    catch (Exception ex)
                    {
                        note = "Error downloading note: " + ex.Message;
                    }

                    string name = (lastname + ", " + firstname + " " + middlename).Trim();

                    dt.Rows.Add(name, mrn, accnum, proc, study_time, creation_time, note, "");

                    rs.MoveNext();
                    pObj.dt = dt;
                    backgroundWorker.ReportProgress(0, pObj);
                }


                rs.Close();
                File.Delete(tempfile);

                int totalNotes = dt.Rows.Count;
                int current = 0;
                foreach (DataRow dr in dt.Rows)
                {
                    // Retrieve reports here

                    query = String.Format(@"select * from storage s,document d,study_document sd,study st
                  where d.id = sd.document_uid 
                    and s.id=d.storage_uid 
                    and st.id=sd.study_uid
                    and d.name='Report'
                    and st.ris_study_euid='{0}'
                    order by d.creation_timedate", dr["Accession"]);
                    //dr["Reports"] = query;

                    result = retrieveRDS(uriFujiRDS, query);

                    if (result == null) return;
                    tempfile = Path.GetTempFileName();
                    ByteArrayToFile(tempfile, result);
                    rs.Open(tempfile, "Provider=MSPersist", ADODB.CursorTypeEnum.adOpenStatic, ADODB.LockTypeEnum.adLockReadOnly, 0);
                    string report = "";

                    while (!rs.EOF)
                    {
                        string http_url = rs.Fields["http_url"].Value.ToString();
                        string https_url = rs.Fields["https_url"].Value.ToString();
                        string doctype = rs.Fields["name"].Value.ToString();
                        string filename = rs.Fields["filename"].Value.ToString();

                        if (url.StartsWith("https"))
                        {
                            filename = https_url + filename;
                        }
                        else
                        {
                            filename = http_url + filename;
                        }

                        try
                        {
                            client.DownloadFile(filename, notefile);
                            report += parseReport(notefile, rbCounty.Checked);
                        }
                        catch (Exception ex)
                        {
                            report += "Error downloading report: " + ex.Message;
                        }


                        rs.MoveNext();

                        if (!rs.EOF) report += "\r\n\r\n======\r\n\r\n";
                    }
                    rs.Close();
                    File.Delete(tempfile);
                    dr["Reports"] = report;

                    current += 1;
                    if ((DateTime.Now - lastprogress).Milliseconds > 50)
                    {
                        backgroundWorker.ReportProgress(current * 100 / totalNotes, pObj);
                        lastprogress = DateTime.Now;
                    }
                }
                File.Delete(notefile);


                pObj.updateDT = true;


                backgroundWorker.ReportProgress(100, pObj);
            }
            catch (Exception ex)
            {
                textBoxNote.Text = ex.Message + "\r\n" + ex.StackTrace;
            }

        }


        public string parseNote(string notefile)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.Load(notefile);

            HtmlAgilityPack.HtmlNodeCollection header_nodes = doc.DocumentNode.SelectNodes("//div[@class=\"clsNoteHeader\"]");
            HtmlAgilityPack.HtmlNodeCollection content_nodes = doc.DocumentNode.SelectNodes("//div[@class=\"clsNoteData\"]");

            string note = "";
            for (int i = 0; i < header_nodes.Count; i++)
            {
                string header = header_nodes[i].InnerText;
                header = HttpUtility.HtmlDecode(header);
                note += header.Trim() + "\r\n\r\n";

                string content = content_nodes[i].InnerText;
                content = HttpUtility.HtmlDecode(content);
                note += content.Trim();
                if (i < (header_nodes.Count - 1))
                {
                    note += "\r\n\r\n====\r\n\r\n";
                }
            }

            return note.Trim();

        }

        public string parseReport(string reportfile, bool county)
        {
            string report_text;
            if (county)
            {
                report_text = File.ReadAllText(reportfile);
            }
            else
            {
                string[] file = File.ReadAllLines(reportfile);
                StringBuilder output = new StringBuilder();
                int startheader = 0, endheader = 0;
                for (int i = 0; i < file.Length; i++)
                {
                    if (file[i].Contains("R a d i o l o g y")) startheader = i;
                    if ((startheader == 0) && file[i].EndsWith("     Imaging"))
                    {
                        endheader = i;
                        break;
                    }
                    if (file[i].EndsWith("     Report"))
                    {
                        endheader = i;
                        break;
                    }
                }
                int skip = 0;
                for (int i = endheader; i < file.Length; i++)
                {
                    int line = (i - startheader) % 61;
                    int page = (i - startheader) / 61;
                    if (line < (endheader - startheader - 1)) continue;
                    if (line > 55) continue;
                    if (file[i].Contains("Patient Loc:")) continue;
                    if (file[i].Contains("DOB:")) continue;
                    if (file[i].Contains("Patient Name:"))
                    {
                        skip = 1;
                        continue;
                    }

                    if (skip > 0)
                    {
                        skip -= 1;
                        continue;
                    }
                    output.Append(file[i] + "\r\n");
                }
                report_text = output.ToString();

            }
            return report_text;
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            btnSave.Enabled = true;
            btnRetrieve.Enabled = true;
            btnLoad.Enabled = true;
            changeFilter();
            dgv_Results.Select();
        }

        private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressObject pObj = e.UserState as ProgressObject;
            if (pObj.updateDT)
            {
                dgv_Results.DataSource = pObj.dt;
            }
            progressBar.Value = e.ProgressPercentage;
        }

        class BackgroundParameter
        {
            public string url;
            public string duration;
        }

        class ProgressObject
        {
            public DataTable dt;
            public bool updateDT = false;
            //public string statusLabel;
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
                DataGridViewCellCollection cells = dgv_Results.SelectedRows[0].Cells;

                if (dgv_Results.Columns.Contains("Note"))
                {
                    textBoxNote.Text = cells["Note"].Value.ToString();
                }
                if (dgv_Results.Columns.Contains("Reports"))
                {
                    textBoxReport.Text = cells["Reports"].Value.ToString();

                    int impression_loc = textBoxReport.Text.ToLower().IndexOf("impression");
                    if (impression_loc > 0)
                    {
                        textBoxReport.SelectionStart = textBoxReport.Text.Length;
                        textBoxReport.ScrollToCaret();
                        textBoxReport.SelectionStart = impression_loc;
                        textBoxReport.ScrollToCaret();
                    }
                }
                if (dgv_Results.Columns.Contains("MRN"))
                {
                    textBoxMRN.Text = cells["MRN"].Value.ToString();
                }
                if (dgv_Results.Columns.Contains("Accession"))
                {
                    textBoxAccession.Text = cells["Accession"].Value.ToString();
                }
            }
            else
            {
                textBoxNote.Text = "";
                textBoxReport.Text = "";
                textBoxMRN.Text = "";
                textBoxAccession.Text = "";
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (dt != null)
            {
                MessageBoxHelper.PrepToCenterMessageBoxOnForm(this);
                DialogResult dr = saveFileDialog.ShowDialog(this);
                if (dr == DialogResult.OK)
                {
                    string outputfile = saveFileDialog.FileName;
                    DataTable dt_out = dt.DefaultView.ToTable();
                    if (dt_out.Columns.Contains("Name"))
                    {
                        dt_out.Columns.Remove("Name");
                    }
                    string s = CsvWriter.WriteToString(dt_out, true, true);

                    TextWriter tw = new StreamWriter(outputfile);
                    tw.WriteLine(s);
                    tw.Close();

                }
            }
        }

        private void changeFilter()
        {
            if (dt != null)
            {
                string input = textBoxFilter.Text.Trim().Replace("'", "''");
                List<string> words = split(input);
                string filter = "";
                for (int i = 0; i < words.Count; i++)
                {
                    if (i > 0) filter += " AND ";
                    filter += "(Note LIKE '*" + words[i] + "*' OR "
                          + "Reports LIKE '*" + words[i] + "*')";
                }

                dt.DefaultView.RowFilter = filter;
            }
        }

        /// <summary>
        /// Splits the string passed in by the delimiters passed in.
        /// Quoted sections are not split, and all tokens have whitespace
        /// trimmed from the start and end.
        public static List<string> split(string stringToSplit)
        {
            List<string> results = new List<string>();

            bool inQuote = false;
            StringBuilder currentToken = new StringBuilder();
            for (int index = 0; index < stringToSplit.Length; ++index)
            {
                char currentCharacter = stringToSplit[index];
                if (currentCharacter == '"')
                {
                    // When we see a ", we need to decide whether we are
                    // at the start or send of a quoted section...
                    inQuote = !inQuote;
                }
                else if (currentCharacter==' ' && inQuote == false)
                {
                    // We've come to the end of a token, so we find the token,
                    // trim it and add it to the collection of results...
                    string result = currentToken.ToString().Trim();
                    if (result != "") results.Add(result);

                    // We start a new token...
                    currentToken = new StringBuilder();
                }
                else
                {
                    // We've got a 'normal' character, so we add it to
                    // the curent token...
                    currentToken.Append(currentCharacter);
                }
            }

            // We've come to the end of the string, so we add the last token...
            string lastResult = currentToken.ToString().Trim();
            if (lastResult != "") results.Add(lastResult);

            return results;
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            DialogResult result = openFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                try
                {
                    DataTable dt_test = new DataTable();
                    using (CsvReader csv = new CsvReader(new StreamReader(openFileDialog.FileName), true))
                    {
                        dt_test.Load(csv);
                    }
                    dt = dt_test;

                    btnSave.Enabled = true;
                    dgv_Results.DataSource = dt;
                    dgv_Results.Select();
                }
                catch
                {

                }
            }
        }

        private void textBoxFilter_TextChanged(object sender, EventArgs e)
        {
            changeFilter();
        }
    }
}
