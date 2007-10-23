using System;
using System.Text;
using System.IO;
using ThoughtWorks.CruiseControl.Core;
using ThoughtWorks.CruiseControl.Core.Util;
using ThoughtWorks.CruiseControl.Core.Tasks;
using Exortech.NetReflector;

/* FinalbuilderTask   
 * 
 * Syntax : (inside tasks block):
 * 
 * <FinalBuilder>
 *		<ProjectFile>C:\TEMP\Project.fbz3</ProjectFile>						 <!-- Required -->
 *		<ShowBanner>false</ShowBanner>										 <!-- Optional, default = true -->
 *		<FBVariables>														 <!-- Optional -->
 *			<FBVariable name="MyVariable" value="SomeValue" />
 *		</FBVariables>
 *		<FBVersion>3</FBVersion>											 <!-- Optional, used to find executable. Default uses extension from project file -->
 *		<FBCMDPath>C:\Program Files\MyFinalBuilderPath\FBCMD.EXE</FBCMDPath> <!-- Optional, overrides FBVersion to provide absolute path to FBCMD.EXE -->
 *		<DontWriteToLog>true</DontWriteToLog>                                <!-- Optional, default = false -->
 *      <UseTemporaryLogFile>true</UseTemporaryLogFile>                      <!-- Optional, default = false, overrides DontWriteToLog -->
 *		<Timeout>100</Timeout>                                               <!-- Optional, in seconds, default = no timeout -->
 * </FinalBuilder>
 *		
 * */

namespace ThoughtWorks.CruiseControl.Core.Tasks
{
	[ReflectorType("FinalBuilder")]
	public class FinalBuilderTask : ITask
	{
		#region Fields

		private ProcessExecutor _executor;
		private IRegistry _registry;	
		private FinalBuilderVersion _fbversion;
		private string _fbcmdpath;
                	
		#endregion

		#region Constructors

		public FinalBuilderTask() : this(new Registry(), new ProcessExecutor()) {}
		
		public FinalBuilderTask(IRegistry registry, ProcessExecutor executor)
		{
			_executor = executor;
			_registry = registry;
			_fbversion = FinalBuilderVersion.FBUnknown;
			_fbcmdpath = String.Empty;
		}

		#endregion

		#region Properties		

		[ReflectorProperty("ProjectFile", Required = true)]
		public string ProjectFile = string.Empty;

		[ReflectorProperty("ShowBanner", Required = false)]
		public bool ShowBanner = false;

		[ReflectorArray("FBVariables", Required= false)] 
		public FBVariable[] FBVariables;
			
		[ReflectorProperty("FBVersion", Required = false)]
		public int FBVersion 
		{
			get 
			{
				if (_fbversion == FinalBuilderVersion.FBUnknown) // Try and autodetect FB Version from project file name
				{
					try
					{
						return Byte.Parse(ProjectFile.Substring(ProjectFile.Length - 1, 1));
					}
					catch
					{
						throw new BuilderException(this, "Finalbuilder version could not be autodetected from project file name.");
					}
				}
				else
				{
					return (int)_fbversion;
				}				
			}
			set
			{
				_fbversion = (FinalBuilderVersion)value;
			}
		}
		
		[ReflectorProperty("FBCMDPath", Required = false)]
		public string FBCMDPath
		{
			get {  return StringUtil.IsBlank(_fbcmdpath) ? GetFBPath() : _fbcmdpath; }
			set {	_fbcmdpath = value;		}
		}

		[ReflectorProperty("DontWriteToLog", Required = false)]
		public bool DontWriteToLog = false;

        [ReflectorProperty("UseTemporaryLogFile", Required = false)]
        public bool UseTemporaryLogFile = false;

        [ReflectorProperty("Timeout", Required = false)]
		public int Timeout = 0;
	
		public void Run(IIntegrationResult result)
		{
            Util.ListenerFile.WriteInfo(result.ListenerFile,
                string.Format("Executing FinalBuilder : BuildFile: {0} ", ProjectFile));


            ProcessResult processResult = AttemptToExecute(NewProcessInfoFrom(result));
			result.AddTaskResult(new ProcessTaskResult(processResult));

			if (processResult.TimedOut)
			{
				throw new BuilderException(this, "Build timed out (after " + Timeout + " seconds)");
			}

            Util.ListenerFile.RemoveListenerFile(result.ListenerFile);
		}

		#endregion

		#region Methods

		protected ProcessResult AttemptToExecute(ProcessInfo info)
		{
			try
			{
				return _executor.Execute(info);
			}
			catch (Exception e)
			{
				throw new BuilderException(this, string.Format("FBCMD unable to execute: {0}\n{1}", info, e), e);
			}
		}

		private ProcessInfo NewProcessInfoFrom(IIntegrationResult result)
		{
			ProcessInfo info = new ProcessInfo(FBCMDPath, GetFBArgs());
			info.TimeOut = Timeout*1000;
            int idx = ProjectFile.LastIndexOf('\\');
            if (idx > -1)
              info.WorkingDirectory = this.ProjectFile.Remove(idx, ProjectFile.Length - idx); // Trim down proj. file to get working dir.
			// Add IntegrationProperties as environment variables
			foreach (string varName in result.IntegrationProperties.Keys)
			{
				object obj1 = result.IntegrationProperties[varName];
				if (obj1 != null)
				{
				  info.EnvironmentVariables.Add(varName,AutoDoubleQuoteString(RemoveTrailingPathDelimeter(obj1.ToString())));
				}
			}           
			return info;
		}

		// Returns arguments to FBCMD.EXE
		private string GetFBArgs()
		{
			StringBuilder args = new StringBuilder();

			if (!ShowBanner)
			{
				args.Append("/B ");
			}

            if (UseTemporaryLogFile)
            {
                args.Append("/TL ");
            }
			else if (DontWriteToLog)
			{
				args.Append("/S ");
			}

			
			if (FBVariables != null && FBVariables.Length > 0)
			{
				args.Append("/V");
				for(int j = 0; j < FBVariables.Length; j++)				
				{					
					args.Append(FBVariables[j].Name);
					args.Append("=");
					args.Append(AutoDoubleQuoteString(FBVariables[j].Value));
					if(j < FBVariables.Length - 1)
					{
						args.Append(";");
					}
					else
					{
						args.Append(" ");
					}
				}								
			}
			
			args.Append("/P");
			args.Append(AutoDoubleQuoteString(ProjectFile));		    
			return args.ToString();
		}	
		
		private string GetFBPath()
		{			
			int fbversion = FBVersion;			
			string keyName = String.Format(@"SOFTWARE\VSoft\FinalBuilder\{0}.0", fbversion); // Works for FB 3 through 5, should work for future versions
	
			string executableDir = _registry.GetLocalMachineSubKeyValue(keyName, "Location");			
			if (StringUtil.IsBlank((executableDir)))
			{
				throw new BuilderException(this, String.Format("Path to Finalbuilder {0} command line executable could not be found.", FBVersion.ToString()));				
			}

			if(fbversion == 3) // FinalBuilder 3 has a different executable name to other versions
			{
				return Path.GetDirectoryName(executableDir) + @"\FB3Cmd.exe";
			}
			else
			{
				return Path.GetDirectoryName(executableDir) + @"\FBCmd.exe";
			}
		}
	
		#endregion

		#region Utility Methods

		private string AutoDoubleQuoteString(string value)
		{
			if (!StringUtil.IsBlank(value) && (value.IndexOf(' ') > -1) && (value.IndexOf("\"") == -1))
			{
				return String.Format("\"{0}\"", value);
			}
			return value;
		}

		private string RemoveTrailingPathDelimeter(string directory)
		{
			return directory.TrimEnd(new char[] { Path.DirectorySeparatorChar });			
		}

		#endregion

		#region FBVariable nested class

		// Nested class for FBVariable entries
		[ReflectorType("FBVariable")]
		public class FBVariable
		{
			private string _name;
			private string _value;

			[ReflectorProperty("name")]
			public string Name
			{
				get { return _name; }
				set { _name = value; }
			}

			[ReflectorProperty("value")]
			public string Value
			{
				get { return _value; }
				set { _value = value; }
			}

			public override string ToString()
			{
				return string.Format("FB Variable: {0} = {1}", Name, Value);
			}

			public FBVariable(string name, string avalue)
			{
				_name = name;
				_value = avalue;
			}

			public FBVariable() { }

		}

		#endregion

		private enum FinalBuilderVersion :int
		{
			FBUnknown = -1,
			FB3 = 3,
			FB4 = 4,
			FB5 = 5
		}

	}
}