using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Data.Entities;
using Jellyfin.Plugin.FinTube.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinTube.Api;

[ApiController]
[Authorize(Policy = "DefaultAuthorization")]
[Route("fintube")]
[Produces(MediaTypeNames.Application.Json)]
public class FinTubeActivityController : ControllerBase
{
        private readonly ILogger<FinTubeActivityController> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _config;
        private readonly IUserManager _userManager;

        public FinTubeActivityController(
            ILoggerFactory loggerFactory,
            IFileSystem fileSystem,
            IServerConfigurationManager config,
            IUserManager userManager)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<FinTubeActivityController>();
            _fileSystem = fileSystem;
            _config = config;
            _userManager = userManager;

            _logger.LogInformation("FinTubeActivityController Loaded");
        }

        public class FinTubeData
        {
            public string ytid {get; set;} = "";
            public string targetfolder{get; set;} = "/tmp";
            public bool audioonly{get; set;} = false;
            public string artist{get; set;} = "";
            public string album{get; set;} = "";
            public string title{get; set;} = "";
            public int track{get; set;} = 0;
        public string addargs { get; set; }
        }

        [HttpPost("submit_dl")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<Dictionary<string, object>> FinTubeDownload([FromBody] FinTubeData data)
        {
            try
            {
                _logger.LogInformation("FinTubeDownload : {ytid} to {targetfoldeer} audio only: {audioonly}, additonal args {addargs}", data.ytid, data.targetfolder, data.audioonly, data.addargs);
                string addargs = data.addargs;
                Dictionary<string, object> response = new Dictionary<string, object>();
                PluginConfiguration? config = Plugin.Instance.Configuration;
                String status = "";


                // check binaries
                if(!System.IO.File.Exists(config.exec_YTDL))
                    throw new Exception("YT-DL Executable configured incorrectly");
                

                bool hasid3v2 = System.IO.File.Exists(config.exec_ID3);
                
                // Create Folder if it doesn't exist
                if(!System.IO.Directory.CreateDirectory(data.targetfolder).Exists)
                    throw new Exception("Directory could not be created");

                // Check for tags
                bool hasTags = 1 < (data.title.Length + data.album.Length + data.artist.Length + data.track.ToString().Length);

                // Save file with ytdlp as mp4 or mp3 depending on audioonly
                String targetFilename;
                String targetExtension = (data.audioonly ? @".mp3" : @".mp4");
                
                if(data.audioonly && hasTags && data.title.Length > 1) // Use title Tag for filename
                    targetFilename = System.IO.Path.Combine(data.targetfolder, $"{data.title}");
                else // Use YTID as filename
                    targetFilename = System.IO.Path.Combine(data.targetfolder, $"{data.ytid}");

                // Check if filename exists
                if(System.IO.File.Exists(targetFilename))
                    throw new Exception($"File {targetFilename} already exists");

                status += $"Filename: {targetFilename}<br>";

                String args;
                if(data.audioonly)
                    args = $"-x --audio-format mp3 -o \"{targetFilename}.%(ext)s\" {data.ytid}";
                else
                    args = $"{addargs} -o \"{targetFilename}-%(title)s.%(ext)s\" {data.ytid}";

                status += $"Exec: {config.exec_YTDL} {args}<br>";

                var procyt = createProcess(config.exec_YTDL, args);
                procyt.Start();
                procyt.WaitForExit();

                // If audioonly AND id3v2 AND tags are set - Tag the mp3 file
                if (data.audioonly && hasid3v2 && hasTags)
                {
                    args = $"-a \"{data.artist}\" -A \"{data.album}\" -t \"{data.title}\" -T \"{data.track}\" \"{targetFilename}{targetExtension}\"";

                    status += $"Exec: {config.exec_ID3} {args}<br>"; 

                    var procid3 = createProcess(config.exec_ID3, args);
                    procid3.Start();
                    procid3.WaitForExit();
                }

                status += "<font color='green'>File Saved!</font>";

                response.Add("message", status);
                return Ok(response);
            }
            catch(Exception e)
            {
                _logger.LogError(e, e.Message);
                return StatusCode(500, new Dictionary<string, object>() {{"message", e.Message}});
            }
        }

        private static Process createProcess(String exe, String args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo() { FileName = exe, Arguments = args };
            return new Process() { StartInfo = startInfo };
        }
}