using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Company.Function
{
    public class AppointmentReminderFunction
    {
        private readonly ILogger _logger;
        private static readonly HttpClient httpClient = new HttpClient();

        public AppointmentReminderFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<AppointmentReminderFunction>();
        }

        [Function("AppointmentReminderFunction")]
        public async Task Run(
            [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, // Adjust the schedule as needed
            FunctionContext context)
        {
            var log = context.GetLogger("AppointmentReminderFunction");

            try
            {
                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

                var appointments = await GetUpcomingAppointmentsAsync();
                if (appointments.Count == 0)
                {
                    log.LogInformation("No upcoming appointments found.");
                    return;
                }
                
                int emailsSent = 0; // Count of emails to show for future log report
                foreach (var appointment in appointments)
                {
                    var appointmentUtcTime = TimeZoneInfo.ConvertTimeToUtc(appointment.datetime, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"));
                    var timeUntilAppointment = appointmentUtcTime - DateTime.UtcNow;

                    if (timeUntilAppointment.TotalHours > 1)
                    {
                        // Skip appointments that are more than an hour away for optimization.
                        log.LogInformation($"Skipping appointment {appointment.aptname} scheduled for {appointmentUtcTime} as it is more than an hour away.");
                        continue;
                    }

                    // log.LogInformation($"Appointment {++appointmentCount}: dateTime: {appointment.datetime}, utc: {appointment.datetime.ToUniversalTime()}, utcnow: {DateTime.UtcNow.ToUniversalTime()}");
                    log.LogInformation($"Time until {appointment.aptname} appointment: {timeUntilAppointment.Hours} hours, {timeUntilAppointment.Minutes} minutes");
                    if (timeUntilAppointment.TotalMinutes <= 30 && timeUntilAppointment.TotalMinutes > 25 )
                    {
                        // Call the "email" logic or API here
                        log.LogInformation($"Sending email reminder for appointment on {appointment.datetime} to {appointment.useremail}");
                        ++emailsSent;
                        await SendEmail(appointment);
                    }
                    else if (timeUntilAppointment.TotalMinutes <= -60)
                    {
                        log.LogInformation($"Deleting appointment {appointment.aptname} scheduled for {appointmentUtcTime}.");
                        // Implement appointment deletion logic here
                        var deleteApiUrl = Environment.GetEnvironmentVariable("DeleteAppointmentApiUrl");
                        if (string.IsNullOrEmpty(deleteApiUrl))
                        {
                            throw new InvalidOperationException("The 'DeleteAppointmentApiUrl' environment variable is not set.");
                        }

                        var deleteRequest = new HttpRequestMessage(HttpMethod.Get, deleteApiUrl);
                        deleteRequest.Content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("userid", appointment.userid),
                            new KeyValuePair<string, string>("aptname", appointment.aptname)
                        });

                        var deleteResponse = await httpClient.SendAsync(deleteRequest);
                        if (!deleteResponse.IsSuccessStatusCode)
                        {
                            log.LogError($"Failed to delete appointment {appointment.aptname} for user {appointment.userid}. Status code: {deleteResponse.StatusCode}");
                        }
                        else
                        {
                            log.LogInformation($"Successfully deleted appointment {appointment.aptname} for user {appointment.userid}.");
                        }
                    }
                }

                if (emailsSent == 0)
                {
                    log.LogInformation($"None of the appointments require reminders.");
                }
                else
                {
                log.LogInformation($"Email reminders sent for all {emailsSent} upcoming appointments.");
                }
                
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred: {ex.Message}");
                log.LogError(ex.ToString());
                throw;
            }
        }

        private static async Task<List<AppointmentData>> GetUpcomingAppointmentsAsync()
        {
            var apiUrl = Environment.GetEnvironmentVariable("AppointmentApiUrl");
            if (string.IsNullOrEmpty(apiUrl))
            {
                throw new InvalidOperationException("The 'AppointmentApiUrl' environment variable is not set.");
            }

            var response = await httpClient.GetStringAsync(apiUrl);
            return JsonConvert.DeserializeObject<List<AppointmentData>>(response)!;
        }

        private static async Task<HttpStatusCode> SendEmail(AppointmentData appointment)
        {
            // Implement email sending logic here

            var apiKey = Environment.GetEnvironmentVariable("SendGridApiKey");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("The 'SendGridApiKey' environment variable is not set.");
            }

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress("appointmentreminder@ember-haviland.com", "Ember Haviland");
            var subject = $"Appointment Reminder: {appointment.aptname}";
            var to = new EmailAddress(appointment.useremail);
            var plainTextContent = $"This is a reminder for your appointment: {appointment.aptname} at {appointment.datetime}. \n Description: {appointment.description} \n Don't forget to bring required materials. \n The appointment will be deleted one hour after the scheduled time.";
            var htmlContent = $"<strong>This is a reminder for your appointment: {appointment.aptname} at {appointment.datetime}.</strong> \n <p>Description: {appointment.description}</p> \n <p>Don't forget to bring required materials.</p> \n <small>The appointment will be deleted one hour after the scheduled time.</small>";
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);

            var response = await client.SendEmailAsync(msg); 
            return response.StatusCode;
        }
    }

    public class AppointmentData
    {
        private string GenerateId()
        {
            return $"{this.userid}-{this.aptname}";
        }

        // Note that "id" must be lower case for the Cosmos APIs to work
        // and for consistency, all keys are lower case
        public string id { get { return GenerateId(); } }

        public string userid { get; set; } = string.Empty;

        public string useremail { get; set; } = string.Empty;
        public string aptname { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public DateTime datetime { get; set; } = DateTime.MinValue;

        public override string ToString()
        {
            return $"id: {id}, userid: {userid}, useremail: {useremail}, aptname: {aptname}, desc: {description}, datetime: {datetime}";
        }
    }
}
