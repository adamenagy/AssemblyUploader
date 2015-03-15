using RestSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Windows.Forms;
using Inv = Inventor;
using System.Collections.Generic; 

namespace ViwerSteps
{
  public partial class Form1 : Form
  {
    public Form1()
    {
      InitializeComponent();
    }

    private void Form1_Load(object sender, EventArgs e)
    {

    }

    const String _serverUrl = "https://developer.api.autodesk.com";
    RestClient _client = new RestClient(_serverUrl);

    String _consumerKey = "";
    String _consumerSecret = "";

    String _token = "";
    string _fileUrn = "";
    void logText(string strText)
    {
      richTextBox1.Text = richTextBox1.Text + strText + "\n";

      richTextBox1.Invalidate();
      this.Invalidate();
    }

    bool authenticate()
    {
      RestRequest request = new RestRequest();
      request.Resource = "/authentication/v1/authenticate";
      request.Method = Method.POST;
      request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
      request.AddParameter("client_id", _consumerKey);
      request.AddParameter("client_secret", _consumerSecret);
      request.AddParameter("grant_type", "client_credentials");

      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        String responseString = response.Content;
        int len = responseString.Length;
        int index = responseString.IndexOf("\"access_token\":\"") + "\"access_token\":\"".Length;
        responseString = responseString.Substring(index, len - index - 1);
        int index2 = responseString.IndexOf("\"");
        _token = responseString.Substring(0, index2);

        logText("Token : " + _token);
        textBox_token.Text = _token;

        // Now set the token.
        request = new RestRequest();
        request.Resource = "/utility/v1/settoken";
        request.Method = Method.POST;
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
        request.AddParameter("access-token", _token);

        response = _client.Execute(request);
        logText(request.Method.ToString() + " " + request.Resource);
        logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
          // Done...
          logText("Set token successfully");

          return true;
        }
      }
      return false;

    }

    // Create the bucket to upload
    bool createBucket(string bucketname)
    {
      RestRequest request = new RestRequest();
      request.Resource = "/oss/v1/buckets";
      request.Method = Method.POST;
      request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
      request.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);

      // Bucketname is the name of the bucket.
      string body = 
        "{\"bucketKey\":\"" + bucketname + 
        "\",\"servicesAllowed\":{},\"policy\":\"transient\"}";
      request.AddParameter("application/json", body, ParameterType.RequestBody);

      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
      {
        logText("Bucket " + bucketname + " already present");

        return false;
      }
      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        logText("Bucket " + bucketname + " created");
        
        return true;
      }

      return false;
    }

    bool uploadFile(string strFile, ref string fileUrn)
    {
      RestRequest request = new RestRequest();

      string strFilename = System.IO.Path.GetFileName(strFile);
      string objectKey = HttpUtility.UrlEncode(strFilename);

      FileStream file = File.Open(strFile, FileMode.Open);
      byte[] fileData = null;
      int nlength = (int)file.Length;
      using (BinaryReader reader = new BinaryReader(file))
      {
        fileData = reader.ReadBytes(nlength);
      }

      request.Resource = "/oss/v1/buckets/" + txtBucketName.Text.ToLower() + "/objects/" + objectKey;
      request.Method = Method.PUT;
      request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
      request.AddParameter("Content-Type", "application/stream");
      request.AddParameter("Content-Length", nlength);
      request.AddParameter("requestBody", fileData, ParameterType.RequestBody);

      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        string responseString = response.Content;

        int len = responseString.Length;
        string id = "\"id\" : \"";
        int index = responseString.IndexOf(id) + id.Length;
        responseString = responseString.Substring(index, len - index - 1);
        int index2 = responseString.IndexOf("\"");
        fileUrn = responseString.Substring(0, index2);

        logText("File " + strFile + " uploaded");
        logText("File id :" + fileUrn);

        return true;
      }
      else
      {
        logText("File " + strFile + " upload failed");

        return false;
      }
    }

    bool registerFile(string strFile, ref string fileUrn)
    {
      byte[] bytes = Encoding.UTF8.GetBytes(fileUrn);
      string urn64 = Convert.ToBase64String(bytes);

      RestRequest request = new RestRequest();
      request.Resource = "/viewingservice/v1/register";
      request.Method = Method.POST;
      request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
      request.AddParameter("Content-Type", "application/json;charset=utf-8", ParameterType.HttpHeader);

      string body = "{\"urn\":\"" + urn64 + "\"}";
      request.AddParameter("application/json", body, ParameterType.RequestBody);

      fileUrn = urn64;
      logText("urn:" + urn64);

      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        // Translation started
        logText("File " + strFile + " Translation started");

        return true;
      }
      else if (response.StatusCode == System.Net.HttpStatusCode.Created)
      {
        // Already present
        logText("File " + strFile + " Translation already present");

        return true;
      }
      else
      {
        // Error

        return false;
      }
    }

    // Upload file, upload children and collect json info,
    // set references 
    bool uploadSubDocs_old(string strFile, ref string fileUrn)
    {
      // 1) Upload file
      if (!uploadFile(strFile, ref fileUrn))
        return false;

      // 2) Get all referenced files and collect json ref
      string strName = Path.GetFileName(strFile); 
      string refJson = 
        "{ " +
          "\"master\" : \"urn:adsk.objects:os.object:alexbicalhobucket2/A1.iam\"," +
          "\"dependencies\" : [";
      Inv.ApprenticeServerComponent app = new Inv.ApprenticeServerComponent();
      Inv.ApprenticeServerDocument doc = app.Open(strFile);
      IList<string> urns = new List<string>();
      Inv.FilesEnumerator files = doc.File.ReferencedFiles;
      foreach (Inv.File subDoc in files)
      {
        string subUrn = "";
        if (uploadSubDocs(subDoc.FullFileName, ref subUrn))
        {
          string strSubName = Path.GetFileName(subDoc.FullFileName);
          refJson += 
            "{ \"file\" : \"urn:adsk.objects:os.object:alexbicalhobucket2/A1A1.iam\"," +
              "\"metadata\" : { " +
              "\"childPath\" : \"" + strSubName + "\"," + 
              "\"parentPath\" : \"" + strName + "\"" +
            "},";
        }
      }

      // 3) Set the reference for the main file
      if (files.Count > 0)
      {
        refJson += "] }";

        RestRequest request = new RestRequest();
        request.Resource = "/oss/v1/setreference";
        request.Method = Method.POST;
        request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
        request.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);

        // Bucketname is the name of the bucket.
        request.AddParameter("application/json", refJson, ParameterType.RequestBody);

        IRestResponse response = _client.Execute(request);
        logText(request.Method.ToString() + " " + request.Resource);
        logText(response.StatusCode.ToString() + ": " + response.StatusDescription);
      }

      return true;
    }


    // Upload children and collect json info,
    // set references 
    bool uploadSubDocs(string strFile, ref string refJson)
    {
      // 1) Get all referenced files and collect json ref
      string strName = Path.GetFileName(strFile);

      // 2) Upload subdocs and collect JSON info
      Inv.ApprenticeServerComponent app = new Inv.ApprenticeServerComponent();
      Inv.ApprenticeServerDocument doc = app.Open(strFile);
      //IList<string> urns = new List<string>();
      Inv.FilesEnumerator files = doc.File.ReferencedFiles;
      foreach (Inv.File file in files)
      {
        string subUrn = "";
        if (uploadFile(file.FullFileName, ref subUrn))
        {
          string strSubName = Path.GetFileName(file.FullFileName);
          refJson +=
            "{ \"file\" : \"" + subUrn + "\"," +
              "\"metadata\" : { " +
            //              "\"childPath\" : \"" + file.FullFileName + "\"," +
            //              "\"parentPath\" : \"" + strFile + "\"" +
              "\"childPath\" : \"" + strSubName + "\"," +
              "\"parentPath\" : \"" + strName + "\"" +
            "},";
        }

        uploadSubDocs(file.FullFileName, ref refJson);
      }

      return true;
    }

    // Upload children and collect json info,
    // set references 
    bool uploadSubDwgs(string strFile, ref string refJson)
    {
       // 1) Get all referenced files and collect json ref
      string strName = Path.GetFileName(strFile);

      // 2) Upload subdocs and collect JSON info

      // Built in name for the time being
      string strSubFile = @"V:\Files\AutoCAD\slave.dwg";
      //foreach (Inv.File file in files)
      {
        string subUrn = "";
        if (uploadFile(strSubFile, ref subUrn))
        {
          string strSubName = Path.GetFileName(strSubFile);
          refJson +=
            "{ \"file\" : \"" + subUrn + "\"," +
              "\"metadata\" : { " +
//              "\"childPath\" : \"" + file.FullFileName + "\"," +
//              "\"parentPath\" : \"" + strFile + "\"" +
              "\"childPath\" : \"" + strSubName + "\"," +
              "\"parentPath\" : \"" + strName + "\"" +
            "},";
        }

        //uploadSubDwgs(file.FullFileName, ref refJson);
      }

      return true;
    }

    // Upload the model.
    bool upload(string strFile, ref string fileUrn)
    {
      // If it's an Inventor assembly then let's get the
      // referenced files too
      if (Path.GetExtension(strFile).ToLower() == ".iam")
      {
        // Upload master
        uploadFile(strFile, ref fileUrn);

        string refJson =
          "{ " +
            "\"master\" : \"" + fileUrn + "\"," +
            "\"dependencies\" : [";

        uploadSubDocs(strFile, ref refJson);

        refJson = refJson.TrimEnd(new char[] { ',' });
        refJson += "] }";

        logText(refJson);

        // Set reference
        RestRequest request = new RestRequest();
        request.Resource = "/references/v1/setreference";
        request.Method = Method.POST;
        request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
        request.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);
        request.AddParameter("application/json", refJson, ParameterType.RequestBody);

        IRestResponse response = _client.Execute(request);
        logText(request.Method.ToString() + " " + request.Resource);
        logText(response.StatusCode.ToString() + ": " + response.StatusDescription);
      }
      else if (Path.GetExtension(strFile).ToLower() == ".dwg")
      {
        // Upload master
        uploadFile(strFile, ref fileUrn);

        string refJson =
          "{ " +
            "\"master\" : \"" + fileUrn + "\"," +
            "\"dependencies\" : [";

        uploadSubDwgs(strFile, ref refJson);

        refJson = refJson.TrimEnd(new char[] { ',' });
        refJson += "] }";

        logText(refJson);

        // Set reference
        RestRequest request = new RestRequest();
        request.Resource = "/references/v1/setreference";
        request.Method = Method.POST;
        request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
        request.AddParameter("Content-Type", "application/json", ParameterType.HttpHeader);
        request.AddParameter("application/json", refJson, ParameterType.RequestBody);

        IRestResponse response = _client.Execute(request);
        logText(request.Method.ToString() + " " + request.Resource);
        logText(response.StatusCode.ToString() + ": " + response.StatusDescription);
      }
      else
      {
        // Upload master
        uploadFile(strFile, ref fileUrn);
      }

      // Just register the file
      // i.e. start translation
      return registerFile(strFile, ref fileUrn);
    }

    void getTheProgress(string fileUrn, bool update)
    {
      RestRequest request = new RestRequest();
      request.Resource = "/viewingservice/v1/" + fileUrn;
      request.Method = Method.GET;
      request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        /*
        dynamic json = SimpleJson.DeserializeObject(response.Content);

        System.Collections.Generic.Dictionary<string, object>.KeyCollection keys = json.Keys;
        System.Collections.Generic.Dictionary<string, object>.ValueCollection Values = json.Values;

        // object title = json.Keys["status"];
        logText("<full_response>");
        for (int i = 0; i < Values.Count; i++)
        {
          var key = keys.ElementAt(i);
          var item = Values.ElementAt(i);

          if (key is string && item is string)
          {
            logText((string)key + "=" + (string)item);

            if (String.Compare((string)key, "progress") == 0)
            {
              label1_per.Text = (string)item;
            }
          }
        }
        logText("</full_response>");
         * */

        logText("<full_response_content>");
        logText(response.Content);
        logText("</full_response_content>");
      }
      else
      {
        logText(response.Content);
      }
    }

    void showThumbnail(string fileUrn)
    {
      RestRequest request = new RestRequest();
      request.Resource = "/viewingservice/v1/thumbnails/" + fileUrn;
      request.Method = Method.GET;
      request.AddParameter("Authorization", "Bearer " + _token, ParameterType.HttpHeader);
      IRestResponse response = _client.Execute(request);
      logText(request.Method.ToString() + " " + request.Resource);
      logText(response.StatusCode.ToString() + ": " + response.StatusDescription);

      if (response.StatusCode == System.Net.HttpStatusCode.OK)
      {
        MemoryStream ms = new MemoryStream(response.RawBytes);
        pictureBox1.Image = Image.FromStream(ms);
        pictureBox1.Invalidate();

        logText("Showing thumbnail");
      }
      else
      {
        logText("Showing thumbnail failed");
      }
    }

    private void button1_Click(object sender, EventArgs e)
    {
      openFileDialog1 = new OpenFileDialog();
      openFileDialog1.Filter = "3d Files (*.stl, *.dwf, *.dwg, *.nwc, *.3ds, *.rvt, *.ipt, *.iam, *.dwfx) | *.stl; *.dwf; *.dwg; *.nwc;*.3ds;*.rvt;*.ipt;*.iam;*.dwfx";
      if (openFileDialog1.ShowDialog() == DialogResult.OK)
      {
        richTextBox1.Text = "";
        label1_filename.Text = openFileDialog1.FileName;
        label1_filename.Invalidate();
        this.Invalidate();
        //

        if (authenticate() == false)
          return;

        string fileUrn = "";
        if (upload(openFileDialog1.FileName, ref fileUrn) == false)
          return;

        showThumbnail(fileUrn);

        getTheProgress(fileUrn, false);

        _fileUrn = fileUrn;
      }
    }

    private void button_status_Click(object sender, EventArgs e)
    {
      showThumbnail(_fileUrn);
      getTheProgress(_fileUrn, true);
    }

    // New token
    private void button2_Click(object sender, EventArgs e)
    {
      // Read the Key & Secret
      _consumerKey = textBox1_key.Text;
      _consumerSecret = textBox_Consumer_Secret.Text;

      logText("");
      logText(" ----------Getting token--------");
      authenticate();
      logText(" ----------Getting token--------");
      logText("");
    }

    private void btnCreateBucket_Click(object sender, EventArgs e)
    {
      if (txtBucketName.Text.Trim() == string.Empty)
      {
        MessageBox.Show("You must input to a bucket name.");
      }

      // Create required bucket
      if (createBucket(txtBucketName.Text.ToLower()) == false)
      {
        logText(" ----------Create Bucket failed--------");
      }
    }

    private void btnViewInBrowser_Click(object sender, EventArgs e)
    {
      // Start the viewer in default browser, this browser should 
      // support WebGL, latest version of Google Chrome or Firefox are recommended.
      string url = string.Format("http://viewer.autodesk.io/node/view-helper?urn={0}&token={1}", _fileUrn, _token);

      Process.Start(url);
    }
  }
}
