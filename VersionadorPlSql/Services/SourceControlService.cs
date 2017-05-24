using System.Configuration;
using System.Data;
using Oracle.DataAccess.Client;
using System.Linq;
using System;
using VersionadorPlSql.DataBase;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Reflection;
using System.Text;
using System.Net;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace VersionadorPlSql.Services
{
    public class SourceControlService
    {
        private static string Oradb = ConfigurationManager.AppSettings["CONNECTION_STRING"];
        private static string Schema = ConfigurationManager.AppSettings["SCHEMA"];
        private static string User = ConfigurationManager.AppSettings["TFS_USER"];
        private static string Password = ConfigurationManager.AppSettings["TFS_USER_PASSWORD"];
        private static string Domain = ConfigurationManager.AppSettings["TFS_USER_DOMAIN"];
        private static string teamCollection = "http://tfsserver:8080/tfs/DefaultCollection"; //TFS URL COLLECTION
        private static string teamProject = "$/[proyect]/Dev/src/ScriptsDB"; //Path folder version control in tfs
        private static string tfsPathWorkspace = AssemblyDirectory + @"Content\MascheWorkspace";
        private static string pathScriptsPl = AssemblyDirectory + @"Content\FilesToVersion";
        private static string nameWorkspace = "MacheWorkSpaceVersionador";

        public static void Start()
        {
            string dataBaseName = string.Empty;

            var packagesTbl = GetPackages(Oradb, Schema, out dataBaseName);

            if (packagesTbl != null)
            {
                ProcessPackages(packagesTbl, dataBaseName);
                CheckInFiles();
            }
        }

        private static void CheckInFiles()
        {
            NetworkCredential netCred = new NetworkCredential(User, Password, Domain);


            TfsTeamProjectCollection tpc = new TfsTeamProjectCollection(new Uri(teamCollection), netCred);

            tpc.Authenticate();

            VersionControlServer versionControl = tpc.GetService<VersionControlServer>();

            // Listen for the Source Control events.
            versionControl.NonFatalError += OnNonFatalError;
            versionControl.Getting += OnGetting;
            versionControl.BeforeCheckinPendingChange += OnBeforeCheckinPendingChange;
            versionControl.NewPendingChange += OnNewPendingChange;

            Workspace workspace = GetWorkspace(versionControl);

            String topDir = null;

            try
            {
                String localDir = tfsPathWorkspace;

                workspace.Map(teamProject, localDir);              

                workspace.Get();

                if (CopyScriptFiles())
                {
                    Workstation.Current.EnsureUpdateWorkspaceInfoCache(versionControl, versionControl.AuthorizedUser);
                    CheckIn(workspace, localDir);
                    UpdateStatusFiles();
                }
            }
            finally
            {
                workspace.Delete();
            }
        }

        private static void CheckIn(Workspace workspace, string localDir)
        {
            try
            {
                workspace.PendAdd(localDir, true);
                PendingChange[] pendingChanges = workspace.GetPendingChanges();
                if (pendingChanges.Any())
                {
                    workspace.CheckIn(pendingChanges, string.Format("Auto version. Date: {0} ", DateTime.Now));
                }
                
            }
            catch (Exception ee)
            {
                if (ee.Message.ToLower().Contains("conflict"))
                {
                    Conflict[] conflicts = workspace.QueryConflicts(new string[] { teamProject }, true);

                    foreach (Conflict conflict in conflicts)
                    {

                        conflict.Resolution = Resolution.AcceptYours;
                        workspace.ResolveConflict(conflict);

                        if (conflict.IsResolved)
                        {
                            workspace.PendEdit(conflict.TargetLocalItem);
                        }
                    }

                    PendingChange[] pendingChanges = workspace.GetPendingChanges();
                    workspace.CheckIn(pendingChanges, string.Format("Auto version. Date: {0} ", DateTime.Now));
                }
                else
                {
                    throw ee;
                }
            }

        }

        private static void UpdateStatusFiles()
        {
            using (var context = new VersionadorPlSqlContext())
            {
                var files = context.FilesSourceControl.Where(x => !x.IsCheckIn).ToList();
                if (files.Any())
                {
                    files.ForEach(f =>
                    {
                        f.IsCheckIn = true;
                    });

                }

                context.SaveChanges();
            }
        }

        private static bool CopyScriptFiles()
        {
            var copy = false;

            using (var context = new VersionadorPlSqlContext())
            {
                var files = context.FilesSourceControl.Where(x => !x.IsCheckIn).ToList();
                if (files.Any())
                {
                   files.ForEach(f =>
                   {
                       if (File.Exists(Path.Combine(pathScriptsPl, f.Name)))
                       {
                           File.Copy(Path.Combine(pathScriptsPl, f.Name), Path.Combine(tfsPathWorkspace, f.Name), true);
                       }
                   });

                    copy = true;
                }
            }

            return copy;
        }
        private static Workspace GetWorkspace(VersionControlServer versionControl)
        {
            Workspace workspace = null;
            try
            {
                workspace = versionControl.CreateWorkspace(nameWorkspace, versionControl.AuthorizedUser);
            }
            catch (Exception)
            {
                workspace = versionControl.GetWorkspace(nameWorkspace, versionControl.AuthorizedUser);
                workspace.Delete();
                workspace = versionControl.CreateWorkspace(nameWorkspace, versionControl.AuthorizedUser);
            }

            return workspace;
        }

        private static void ProcessPackages(DataTable packagesTbl, string dataBaseName)
        {
            var files = new List<FileSourceControl>();
            CleanLocalFiles();

            using (var context = new VersionadorPlSqlContext())
            {
                files = context.FilesSourceControl.ToList();


                foreach (DataRow package in packagesTbl.Rows)
                {
                    var fileName = string.Format("{0}.{1}.{2}.sql", dataBaseName, package["OWNER"], package["OBJECT_NAME"]);

                    if (!files.Any(f => f.Name == fileName))
                    {
                        context.FilesSourceControl.Add(new FileSourceControl()
                        {
                            CodeCreated = DateTime.Parse(package["CREATED"].ToString()),
                            FileCreated = DateTime.Now,
                            FileLastModify = DateTime.Parse(package["LAST_DDL_TIME"].ToString()),
                            Name = fileName,
                            IsCheckIn = false
                        });

                        CreateFile(package["OBJECT_NAME"].ToString(), fileName);

                    }
                    else
                    {
                        var file = files.First(f => f.Name == fileName);
                        var lastModifyDb = DateTime.Parse(package["LAST_DDL_TIME"].ToString());

                        if (file.FileLastModify < lastModifyDb)
                        {
                            file.FileLastModify = lastModifyDb;
                            file.IsCheckIn = false;

                            CreateFile(package["OBJECT_NAME"].ToString(), fileName);
                        }
                    }
                }

                context.SaveChanges();
            }
            //var files = 
        }

        private static void CleanLocalFiles()
        {
            DirectoryInfo di = new DirectoryInfo(pathScriptsPl);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }

        private static void CreateFile(string packageName, string fileName)
        {
            using (StreamWriter _testData = new StreamWriter(Path.Combine(pathScriptsPl, fileName), false))
            {
                string script = GetScriptPackage(packageName);
                _testData.WriteLine(script); // Write the file.
            }
        }

        private static string GetScriptPackage(string packageName)
        {
            var response = new StringBuilder();
            using (OracleConnection conn = new OracleConnection(Oradb))
            {
                conn.Open();
                OracleCommand cmd = new OracleCommand();

                cmd.Connection = conn;

                cmd.CommandText = string.Format("select text from all_source where name = '{0}'  and type = 'PACKAGE BODY' and  owner = '{1}' order by line", packageName, Schema);

                cmd.CommandType = CommandType.Text;

                OracleDataReader dr = cmd.ExecuteReader();

                while (dr.Read())
                {
                    response.Append(dr.GetString(0));
                }

                conn.Dispose();
            }

            return response.ToString();
        }

        public static string AssemblyDirectory
        {
            get
            { 
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static DataTable GetPackages(string oradb, string schema, out string dataBaseName)
        {
            var packagesQry = new DataSet();
            using (OracleConnection conn = new OracleConnection(oradb))
            {
                conn.Open();
                dataBaseName = conn.DataSource;
                OracleCommand cmd = new OracleCommand();

                cmd.Connection = conn;

                cmd.CommandText = string.Format("SELECT * FROM ALL_OBJECTS WHERE OBJECT_TYPE IN ('PACKAGE') and owner='{0}'", schema);

                cmd.CommandType = CommandType.Text;

                OracleDataReader dr = cmd.ExecuteReader();

                using (OracleDataAdapter dataAdapter = new OracleDataAdapter())
                {
                    dataAdapter.SelectCommand = cmd;
                    dataAdapter.Fill(packagesQry);
                }
                conn.Dispose();
            }

            if (packagesQry.Tables.Count > 0)
            {
                return packagesQry.Tables[0];
            }

            return null;
        }

        internal static void OnNonFatalError(Object sender, ExceptionEventArgs e)
        {
            if (e.Exception != null)
            {
                Console.Error.WriteLine("  Non-fatal exception: " + e.Exception.Message);
            }
            else
            {
                Console.Error.WriteLine("  Non-fatal failure: " + e.Failure.Message);
            }
        }

        private static void OnGetting(Object sender, GettingEventArgs e)
        {
            Console.WriteLine("  Getting: " + e.TargetLocalItem + ", status: " + e.Status);
        }

        private static void OnBeforeCheckinPendingChange(Object sender, ProcessingChangeEventArgs e)
        {
            Console.WriteLine("  Checking in " + e.PendingChange.LocalItem);
        }

        private static void OnNewPendingChange(Object sender, PendingChangeEventArgs e)
        {
            Console.WriteLine("  Pending " + PendingChange.GetLocalizedStringForChangeType(e.PendingChange.ChangeType) +
                              " on " + e.PendingChange.LocalItem);
        }
    }
}