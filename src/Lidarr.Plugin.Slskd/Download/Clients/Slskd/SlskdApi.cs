using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public class SlskdOptionsDirectories
    {
        [JsonProperty("incomplete")]
        public string Incomplete { get; set; }

        [JsonProperty("downloads")]
        public string Downloads { get; set; }
    }

    public class SlskdOptions
    {
        [JsonProperty("directories")]
        public SlskdOptionsDirectories Directories { get; set; }
    }

    public class SlskdSearchRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("fileLimit")]
        public int FileLimit { get; set; }

        [JsonProperty("filterResponses")]
        public bool FilterResponses { get; set; }

        [JsonProperty("maximumPeerQueueLength")]
        public int MaximumPeerQueueLength { get; set; }

        [JsonProperty("minimumPeerUploadSpeed")]
        public int MinimumPeerUploadSpeed { get; set; }

        [JsonProperty("minimumResponseFileCount")]
        public int MinimumResponseFileCount { get; set; }

        [JsonProperty("responseLimit")]
        public int ResponseLimit { get; set; }

        [JsonProperty("searchText")]
        public string SearchText { get; set; }

        [JsonProperty("searchTimeout")]
        public int SearchTimeout { get; set; }
    }

    public class SlskdSearchEntry
    {
        [JsonProperty("endedAt")]
        public DateTime? EndedAt { get; set; }

        [JsonProperty("fileCount")]
        public int FileCount { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }

        [JsonProperty("lockedFileCount")]
        public int LockedFileCount { get; set; }

        [JsonProperty("responseCount")]
        public int ResponseCount { get; set; }

        [JsonProperty("responses")]
        public List<SlskdResponse> Responses { get; set; }

        [JsonProperty("searchText")]
        public string SearchText { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("state")]
        public SlskdFileStatus State { get; set; }

        [JsonProperty("token")]
        public int Token { get; set; }
    }

    public class SlskdResponseFile
    {
        [JsonProperty("bitDepth")]
        public int? BitDepth { get; set; }

        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("length")]
        public int? Length { get; set; }

        [JsonProperty("sampleRate")]
        public int? SampleRate { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("isLocked")]
        public bool IsLocked { get; set; }

        [JsonProperty("bitRate")]
        public int? BitRate { get; set; }

        [JsonProperty("isVariableBitRate")]
        public bool? IsVariableBitRate { get; set; }
    }

    public class SlskdResponse
    {
        [JsonProperty("fileCount")]
        public int FileCount { get; set; }

        [JsonProperty("files")]
        public List<SlskdResponseFile> Files { get; set; }

        [JsonProperty("hasFreeUploadSlot")]
        public bool HasFreeUploadSlot { get; set; }

        [JsonProperty("lockedFileCount")]
        public int LockedFileCount { get; set; }

        [JsonProperty("lockedFiles")]
        public List<SlskdResponseFile> LockedFiles { get; set; }

        [JsonProperty("queueLength")]
        public int QueueLength { get; set; }

        [JsonProperty("token")]
        public int Token { get; set; }

        [JsonProperty("uploadSpeed")]
        public int UploadSpeed { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }
    }

    public class SlskdDownloadEntry
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("directories")]
        public List<SlskdDirectory> Directories { get; set; }
    }

    public class SlskdDirectory
    {
        [JsonProperty("directory")]
        public string Directory { get; set; }

        [JsonProperty("fileCount")]
        public int FileCount { get; set; }

        [JsonProperty("files")]
        public List<SlskdFile> Files { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SlskdFileStatus
    {
        [EnumMember(Value = "None")]
        None,
        [EnumMember(Value = "Initializing")]
        Initializing,
        [EnumMember(Value = "Requested")]
        Requested,
        [EnumMember(Value = "Queued, Remotely")]
        QueuedRemotely,
        [EnumMember(Value = "Queued, Locally")]
        QueuedLocally,
        [EnumMember(Value = "InProgress")]
        InProgress,
        [EnumMember(Value = "Completed, Succeeded")]
        CompletedSucceeded,
        [EnumMember(Value = "Completed, Cancelled")]
        CompletedCancelled,
        [EnumMember(Value = "Completed, TimedOut")]
        CompletedTimedOut,
        [EnumMember(Value = "Completed, ResponseLimitReached")]
        CompletedResponseLimitReached,
        [EnumMember(Value = "Completed, FileLimitReached")]
        CompletedFileLimitReached,
        [EnumMember(Value = "Completed, Errored")]
        CompletedErrored,
        [EnumMember(Value = "Completed, Rejected")]
        CompletedRejected
    }

    public class SlskdFile
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("startOffset")]
        public int StartOffset { get; set; }

        [JsonProperty("state")]
        public SlskdFileStatus State { get; set; }

        [JsonProperty("requestedAt")]
        public DateTime RequestedAt { get; set; }

        [JsonProperty("enqueuedAt")]
        public DateTime EnqueuedAt { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("endedAt")]
        public DateTime EndedAt { get; set; }

        [JsonProperty("bytesTransferred")]
        public int BytesTransferred { get; set; }

        [JsonProperty("averageSpeed")]
        public double AverageSpeed { get; set; }

        [JsonProperty("bytesRemaining")]
        public int BytesRemaining { get; set; }

        [JsonProperty("elapsedTime")]
        public TimeSpan ElapsedTime { get; set; }

        [JsonProperty("percentComplete")]
        public float PercentComplete { get; set; }

        [JsonProperty("remainingTime")]
        public TimeSpan RemainingTime { get; set; }
    }
}
