﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using DataBridge.Extensions;
using DataBridge.Helper;

namespace DataBridge
{
    [Serializable]
    public class Pipeline : IDisposable
    {
        // ************************************ Member **********************************************

        private List<DataCommand> commands = new List<DataCommand>();
        private bool useStreaming = true;
        private int streamingBlockSize = 100000;
        private string name = "";
        private string workingDirectory = @".\";
        private IPipelineExecuter executer;

        // ************************************ Constructor **********************************************

        public Pipeline()
        {
            this.executer = new PipelineExecuter(this);
            this.executer.OnExecuteCommand += this.Executer_OnExecuteCommand;
            this.executer.OnExecutionCanceled += this.Executer_OnExecutionCanceled;
        }

        public Pipeline(string name)
        {
            this.executer = new PipelineExecuter(this);
            this.executer.OnExecuteCommand += this.Executer_OnExecuteCommand;
            this.executer.OnExecutionCanceled += this.Executer_OnExecutionCanceled;
            this.Name = name;
        }

        // ************************************ Properties **********************************************

        public event Action<DataCommand> OnExecuteCommand;

        public event EventHandler<EventArgs<string>> OnExecutionCanceled;

        [XmlArray("Commands", IsNullable = false)]
        public List<DataCommand> Commands
        {
            get { return this.commands; }
            set { this.commands = value; }
        }

        [XmlAttribute]
        public bool UseStreaming
        {
            get { return this.useStreaming; }
            set { this.useStreaming = value; }
        }

        [XmlAttribute]
        public int StreamingBlockSize
        {
            get { return this.streamingBlockSize; }
            set { this.streamingBlockSize = value; }
        }

        [XmlIgnore]
        public string Name
        {
            get { return this.name; }
            protected set { this.name = value; }
        }

        [XmlAttribute]
        public string WorkingDirectory
        {
            get { return this.workingDirectory; }
            set { this.workingDirectory = value; }
        }

        [XmlIgnore]
        public DateTime? BlackoutStart { get; set; }

        [XmlAttribute(DataType = "string")]
        [Browsable(false)]
        public string BlackoutStartTime
        {
            get { return this.BlackoutStart.HasValue ? this.BlackoutStart.ToString("hh:mi:ss") : string.Empty; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    this.BlackoutStart = DateTimeUtil.TryParseExact(value, "hh:mi:ss");
                }
            }
        }

        [XmlIgnore]
        public DateTime? BlackoutEnd { get; set; }

        [XmlAttribute(DataType = "string")]
        [Browsable(false)]
        public string BlackoutEndTime
        {
            get { return this.BlackoutEnd.HasValue ? this.BlackoutEnd.ToString("hh:mi:ss") : string.Empty; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    this.BlackoutEnd = DateTimeUtil.TryParseExact(value, "hh:mi:ss");
                }
            }
        }

        [XmlIgnore]
        public bool IsInBlackout
        {
            get
            {
                if (!this.BlackoutStart.HasValue)
                {
                    return false;
                }

                if (!this.BlackoutEnd.HasValue)
                {
                    return false;
                }

                if (this.BlackoutStart.Value.TimeOfDay == TimeSpan.Zero &&
                    this.BlackoutEnd.Value.TimeOfDay == TimeSpan.Zero)
                {
                    return false;
                }

                if (DateTime.Now.TimeOfDay.IsInTimeRange(this.BlackoutStart.Value.TimeOfDay, this.BlackoutEnd.Value.TimeOfDay))
                {
                    return true;
                }

                return false;
            }
        }

        // ************************************ Functions **********************************************

        public static IEnumerable<DataCommand> GetAllAvailableCommands()
        {
            var availableCommands = typeof(DataCommand)
                                    .Assembly.GetTypes()
                                    .Where(t => t.IsSubclassOf(typeof(DataCommand)) && !t.IsAbstract)
                                    .Select(t => (DataCommand)Activator.CreateInstance(t));

            return availableCommands;
        }

        public static void Save(string fileName, Pipeline pipeline)
        {
            var serializer = new XmlSerializerHelper<Pipeline>();
            serializer.Save(fileName, pipeline);
        }

        public static Pipeline Load(string fileName)
        {
            var serializer = new XmlSerializerHelper<Pipeline>();
            var pipeline = serializer.Load(fileName);
            pipeline.Name = Path.GetFileNameWithoutExtension(fileName);
            foreach (var command in pipeline.Commands)
            {
                command.WeakPipeLine = new WeakReference<Pipeline>(pipeline);
            }
            return pipeline;
        }

        public static void LoadAndExecute(string fileName)
        {
            var pipeline = Load(fileName);
            pipeline.ExecutePipeline();
        }

        public bool ExecutePipeline(CommandParameters inParameters = null)
        {
            return executer.ExecutePipeline(inParameters);
        }

        public void StopPipeline()
        {
            //LogManager.Instance.LogDebugFormat(this.GetType(), "Deinitialize pipeline '{0}'", this.Name);

            foreach (var command in this.Commands)
            {
                command.DeInitialize();
            }
        }

        public List<string> ValidatePipeline()
        {
            var validationResults = new List<string>();
            var allCommands = this.Commands.Traverse(x => x.Commands);

            foreach (var command in allCommands)
            {
                var messages = command.Validate(null, ValidationContext.Static);
                foreach (var message in messages)
                {
                    validationResults.Add(command.GetType().Name + ": " + message);
                }
            }

            return validationResults;
        }

        public void Dispose()
        {
            if (this.commands != null)
            {
                foreach (var command in this.commands)
                {
                    command.Dispose();
                }

                this.commands.Clear();
            }

            if (this.executer != null)
            {
                this.executer.OnExecuteCommand -= this.Executer_OnExecuteCommand;
                this.executer.OnExecutionCanceled -= this.Executer_OnExecutionCanceled;
                this.executer = null;
            }
        }

        public bool Equals(Pipeline other)
        {
            return string.Equals(this.Name, other.Name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (obj.GetType() != this.GetType())
            {
                return false;
            }
            return this.Equals((Pipeline)obj);
        }

        public override int GetHashCode()
        {
            return (this.Name != null ? this.Name.GetHashCode() : 0);
        }

        private void Executer_OnExecutionCanceled(object sender, EventArgs<string> e)
        {
            if (this.OnExecutionCanceled != null)
            {
                this.OnExecutionCanceled(sender, e);
            }
        }

        private void Executer_OnExecuteCommand(DataCommand dataCommand)
        {
            if (this.OnExecuteCommand != null)
            {
                this.OnExecuteCommand(dataCommand);
            }
        }
    }
}