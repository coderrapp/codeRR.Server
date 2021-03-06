﻿using System;

namespace Coderr.Server.Domain.Core.Feedback
{
    /// <summary>
    ///     Feedback written by the user when an exception was thrown
    /// </summary>
    public class UserFeedback
    {
        /// <summary>
        ///     Application that the feedback is for
        /// </summary>
        public int ApplicationId { get; private set; }

        /// <summary>
        ///     Feedback entry can be removed.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         We can receive feedback before the exception have been uploaded. In those situations we need to wait on the
        ///         error report
        ///         before we know what incident the feedback belongs to. But since we do not want a lot of junk in our tables we
        ///         keep
        ///         unidentified feedback entries just for a couple of days.
        ///     </para>
        /// </remarks>
        public bool CanRemove => ApplicationId == 0 && DateTime.Now.Subtract(CreatedAtUtc).TotalDays > 5;


        /// <summary>
        ///     Can only update the entry if we've been associated to a report+application.
        /// </summary>
        public bool CanUpdate => ApplicationId != 0
                                 || ReportId != 0;

        /// <summary>
        ///     When the feebback was created by the client library
        /// </summary>
        public DateTime CreatedAtUtc { get; private set; }

        /// <summary>
        ///     Description written by the user (of what he/she did when the exception was created).
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        ///     Email address if the user want to get notified of progress.
        /// </summary>
        public string EmailAddress { get; private set; }

        /// <summary>
        ///     The unique error id that was generated by the client library
        /// </summary>
        public string ErrorId { get; private set; }

        /// <summary>
        ///     PK
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        ///     Incident that the feedback was created for
        /// </summary>
        public int IncidentId { get; private set; }

        /// <summary>
        ///     PK for the report in our DB
        /// </summary>
        public int ReportId { get; private set; }

        /// <summary>
        ///     We've identified which report this feedback belongs to
        /// </summary>
        /// <param name="reportId">Report PK, can be null if we do not store the report that the feedback came with</param>
        /// <param name="incidentId">Incident that the report belongs to</param>
        /// <param name="applicationId">Application that the incident belongs to</param>
        public void AssignToReport(int reportId, int incidentId, int applicationId)
        {
            if (incidentId <= 0) throw new ArgumentOutOfRangeException("incidentId");
            if (applicationId <= 0) throw new ArgumentOutOfRangeException("applicationId");

            ReportId = reportId;
            IncidentId = incidentId;
            ApplicationId = applicationId;
        }
    }
}
