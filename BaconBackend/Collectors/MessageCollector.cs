﻿using BaconBackend.DataObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaconBackend.Helpers;
using System.Net;

namespace BaconBackend.Collectors
{
    public class MessageCollector : Collector<Message>
    {
        private BaconManager m_baconMan;

        public MessageCollector(BaconManager baconMan) :
            base(baconMan, "messageInbox")
        {
            m_baconMan = baconMan;

            // Set up the list helper
            InitListHelper("/message/inbox/.json");
        }

        /// <summary>
        /// Fired when the messages should be formatted.
        /// </summary>
        /// <param name="messages"></param>
        protected override void ApplyCommonFormatting(ref List<Message> messages)
        {
            foreach(Message message in messages)
            {
                // Decode
                message.Subject = WebUtility.HtmlDecode(message.Subject);
                message.Body = WebUtility.HtmlDecode(message.Body);

                // Set the first line
                message.HeaderFirst = message.Subject;

                // Set the UI strings
                if (message.WasComment)
                {
                    message.HeaderSecond = message.Author + " via " + message.Subreddit;
                }
                else
                {
                    message.HeaderSecond = message.Author;
                }
            }
        }

        protected override List<Message> ParseElementList(List<Element<Message>> elements)
        {
            // Converts the elements into a list.
            List<Message> messages = new List<Message>();
            foreach (Element<Message> element in elements)
            {
                messages.Add(element.Data);
            }
            return messages;
        }

        #region Message Actions

        /// <summary>
        /// Called by the consumer when a massage should be changed
        /// </summary>
        public void ChangeMessageReadStatus(Message message, bool isRead, int messagePosition = 0)
        {
            // Using the post and suggested index, find the real post and index
            Message collectionMessage = message;
            FindMessageInCurrentCollection(ref collectionMessage, ref messagePosition);

            if (collectionMessage == null || messagePosition == -1)
            {
                // We didn't find it.
                return;
            }

            // Update the status
            collectionMessage.IsNew = isRead;

            // Fire off that a update happened.
            FireCollectionUpdated(messagePosition, new List<Message>() { collectionMessage }, false, false);

            // Start a task to make the vote
            Task.Run(async () =>
            {
                try
                {
                    // Build the data
                    string request = collectionMessage.IsNew ? "/api/unread_message" : "/api/read_message";
                    List<KeyValuePair<string, string>> postData = new List<KeyValuePair<string, string>>();
                    postData.Add(new KeyValuePair<string, string>("id", collectionMessage.GetFullName()));

                    // Make the call
                    string str = await m_baconMan.NetworkMan.MakeRedditPostRequest(request, postData);

                    // Do some super simple validation
                    if (str != "{}")
                    {
                        throw new Exception("Failed to set message status! The response indicated a failure");
                    }

                    // Check to see if there are any unread messages
                    List<Message> messages = GetCurrentPostsInternal();
                    bool foundUnread = false;
                    foreach(Message searchMessage in messages)
                    {
                        if(searchMessage.IsNew)
                        {
                            foundUnread = true;
                            break;
                        }
                    }

                    // Update user man to the UI won't reflect incorrectly.
                    m_baconMan.UserMan.ToggleHasMessages(foundUnread);
                }
                catch (Exception ex)
                {
                    m_baconMan.MessageMan.DebugDia("failed to set message status!", ex);
                    m_baconMan.TelemetryMan.ReportUnExpectedEvent(this, "failedToSetMessageRead", ex);
                }
            });
        }

        /// <summary>
        /// Given a post and a starting index this function will return the collection message
        /// object and the true index of the item. If not found it will return null and -1
        /// </summary>
        private void FindMessageInCurrentCollection(ref Message message, ref int index)
        {
            // Get the current list
            List<Message> messages = GetCurrentPostsInternal();

            // Find the message starting at the possible index
            for (; index < messages.Count; index++)
            {
                if (messages[index].Id.Equals(message.Id))
                {
                    // Grab the message and break;
                    message = messages[index];
                    return;
                }
            }

            // If we didn't find it kill them.
            index = -1;
            message = null;
        }

        #endregion
    }
}
