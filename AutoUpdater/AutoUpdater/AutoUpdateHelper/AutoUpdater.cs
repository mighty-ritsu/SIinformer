/*****************************************************************
 * Copyright (C) Knights Warrior Corporation. All rights reserved.
 * 
 * Author:   圣殿骑士（Knights Warrior） 
 * Email:    KnightsWarrior@msn.com
 * Website:  http://www.cnblogs.com/KnightsWarrior/       http://knightswarrior.blog.51cto.com/
 * Create Date:  5/8/2010 
 * Usage:
 *
 * RevisionHistory
 * Date         Author               Description
 * 
*****************************************************************/
using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks;
using AutoUpdater.AutoUpdateHelper;
using System.Linq;

namespace KnightsWarriorAutoupdater
{
    #region The delegate
    public delegate void ShowHandler();
    #endregion

    public class AutoUpdater : IAutoUpdater
    {
        #region The private fields
        private Config config = null;
        private bool bNeedRestart = false;
        private bool bDownload = false;
        List<DownloadFileInfo> downloadFileListTemp = null;
        #endregion

        #region The public event
        public event ShowHandler OnShow;
        #endregion

        #region The constructor of AutoUpdater
        public AutoUpdater()
        {
            config = Config.LoadConfig(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConstFile.FILENAME));
        }
        #endregion

        #region The public method

        /// <summary>
        /// проверка наличия обновления, и если оно есть - получить информацию по нему
        /// </summary>
        /// <returns></returns>
        public void CheckUpdates(Action<UpdateInfo> done)
        {
            if (done == null) return;            
            // запускаем проверку в отдельном потоке
            Task.Factory.StartNew(() =>
                {
                    done(_CheckUpdates());// возвращаем результат проверки
                });
        }
        
        private UpdateInfo _CheckUpdates()
        {
            UpdateInfo ret = new UpdateInfo() { hasUpdate = false, updateInfo = "" };
            if (!config.Enabled) return ret;

            Dictionary<string, RemoteFile> listRemotFile = ParseRemoteXml(config.ServerUrl);

            List<DownloadFileInfo> downloadList = new List<DownloadFileInfo>();

            foreach (LocalFile file in config.UpdateFileList)
            {
                if (listRemotFile.ContainsKey(file.Path))
                {
                    RemoteFile rf = listRemotFile[file.Path];
                    Version v1 = new Version(rf.LastVer);
                    Version v2 = new Version(file.LastVer);
                    if (v1 > v2)
                    {
                        downloadList.Add(new DownloadFileInfo(rf.Url, file.Path, rf.LastVer, rf.Size, rf.Description));
                        file.LastVer = rf.LastVer;
                        file.Size = rf.Size;

                        if (rf.NeedRestart)
                            bNeedRestart = true;
                      
                    }

                    listRemotFile.Remove(file.Path);
                }
            }

            foreach (RemoteFile file in listRemotFile.Values)
            {
                // если нет файла в локальных настройках, то есть он новый, то добавим
                if (config.UpdateFileList.Where(x => x.Path == file.Path).FirstOrDefault() == null)                
                    config.UpdateFileList.Add(new LocalFile(file.Path, file.LastVer, file.Size));
                

                downloadList.Add(new DownloadFileInfo(file.Url, file.Path, file.LastVer, file.Size, file.Description));

                if (file.NeedRestart)
                    bNeedRestart = true;
            }
            bDownload = downloadList.Count > 0;
            downloadFileListTemp = downloadList;

            
            if (bDownload)
            {
                ret.hasUpdate = true;
                foreach (var file in downloadList)
                {
                    ret.updateInfo = string.Format("{0}{1}({2}):\r\n{3}\r\n\r\n", ret.updateInfo, file.FileName, file.LastVer, file.Description);
                }
            }
            return ret;
        }

        DownloadingInfo _DownloadingInfo = null;
        public void Update(DownloadingInfo downloadingInfo)
        {
            _DownloadingInfo = downloadingInfo;
            // повторно проверяем наличие обновлений, чтобы не попасть в промежуток, если юзер долго не хотел обновляться после получения информации об обновлении
            CheckUpdates((info) =>
            {
                if (info.hasUpdate)
                {
                    StartDownload(downloadFileListTemp);                    
                }
            });

           
        }

        public void RollBack()
        {
            foreach (DownloadFileInfo file in downloadFileListTemp)
            {
                string tempUrlPath = CommonUnitity.GetFolderUrl(file);
                string oldPath = string.Empty;
                try
                {
                    if (!string.IsNullOrEmpty(tempUrlPath))
                    {
                        oldPath = Path.Combine(CommonUnitity.SystemBinUrl + tempUrlPath.Substring(1), file.FileName);
                    }
                    else
                    {
                        oldPath = Path.Combine(CommonUnitity.SystemBinUrl, file.FileName);
                    }

                    if (oldPath.EndsWith("_"))
                        oldPath = oldPath.Substring(0, oldPath.Length - 1);

                    MoveFolderToOld(oldPath + ".old", oldPath);

                }
                catch (Exception ex)
                {
                    //log the error message,you can use the application's log code
                }
            }
        }


        #endregion

        #region The private method
        string newfilepath = string.Empty;
        private void MoveFolderToOld(string oldPath, string newPath)
        {
            if (File.Exists(oldPath) && File.Exists(newPath))
            {
                System.IO.File.Copy(oldPath, newPath, true);
            }
        }

        private void StartDownload(List<DownloadFileInfo> downloadList)
        {
            //DownloadProgress dp = new DownloadProgress(downloadList);
            //if (dp.ShowDialog() == DialogResult.OK)
            //{
            //    //
            //    if (DialogResult.Cancel == dp.ShowDialog())
            //    {
            //        return;
            //    }


            DownloadEngine de = new DownloadEngine(downloadList, _DownloadingInfo, (success) =>
                {
                    if (success)
                    {
                        //Update successfully
                        config.SaveConfig(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConstFile.FILENAME));

                        if (bNeedRestart)
                        {
                            //Delete the temp folder
                            Directory.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConstFile.TEMPFOLDERNAME), true);

                            MessageBox.Show(ConstFile.APPLYTHEUPDATE, ConstFile.MESSAGETITLE, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            CommonUnitity.RestartApplication();
                        }
                    }
                }
                );
               
            //}
        }

        private Dictionary<string, RemoteFile> ParseRemoteXml(string xml)
        {
            XmlDocument document = new XmlDocument();
            document.Load(xml);

            Dictionary<string, RemoteFile> list = new Dictionary<string, RemoteFile>();
            foreach (XmlNode node in document.DocumentElement.ChildNodes)
            {
                list.Add(node.Attributes["path"].Value, new RemoteFile(node));
            }

            return list;
        }
        #endregion

    }

}
