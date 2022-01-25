using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using PatientManagement.AdmissionDischargeTransfer.Commands;
using PatientManagement.Framework;
using PatientManagement.Framework.Commands;
using Polly;
using Polly.Retry;
using ProjectionManager;
using Xunit;

namespace PatientManagement.Tests;

public class PatientManagementTests
{
    private readonly RetryPolicy retryPolicy =
        Policy.Handle<Exception>().WaitAndRetry(5, i => TimeSpan.FromSeconds(i));
    
    [Fact]
    public async Task EndToEndTest()
    {
        var eventStoreConnection = GetEventStoreConnection();
        var dispatcher = SetupDispatcher(eventStoreConnection);
        var connectionFactory = new ConnectionFactory("PatientManagement");

        var projections = new List<IProjection>
        {
            new WardViewProjection(connectionFactory),
            new PatientDemographicProjection(connectionFactory)
        };

        var projectionManager = new ProjectionManager.ProjectionManager(
            eventStoreConnection,
            connectionFactory,
            projections);

        projectionManager.Start();
        
        var patientId = Guid.NewGuid();

        var admitPatient = new AdmitPatient(patientId, "Tony Ferguson", 32, DateTime.UtcNow, 10);
        await dispatcher.Dispatch(admitPatient);

        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.NotNull(patient);
            Assert.Equal(patientId.ToString(), patient.Id);
            Assert.Equal(admitPatient.PatientName, patient.PatientName);
            Assert.Equal(admitPatient.AgeInYears, patient.AgeInYears);
            Assert.Equal(admitPatient.WardNumber, patient.WardNumber);
        });

        var transferPatientOne = new TransferPatient(patientId, 76);
        await dispatcher.Dispatch(transferPatientOne);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.NotNull(patient);
            Assert.Equal(transferPatientOne.WardNumber, patient.WardNumber);
        });

        var transferPatientTwo = new TransferPatient(patientId, 34);
        await dispatcher.Dispatch(transferPatientTwo);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.NotNull(patient);
            Assert.Equal(transferPatientTwo.WardNumber, patient.WardNumber);
        });

        var dischargePatient = new DischargePatient(patientId);
        await dispatcher.Dispatch(dischargePatient);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.Null(patient);
        });
    }
    
    static IEventStoreConnection GetEventStoreConnection()
    {
        const string connectionString = 
            "ConnectTo=tcp://localhost:1113;UseSslConnection=false;";
        var eventStoreConnection = EventStoreConnection.Create(connectionString);

        eventStoreConnection.ConnectAsync().Wait();
        return eventStoreConnection;
    }

    Dispatcher SetupDispatcher(IEventStoreConnection eventStoreConnection)
    {
        var repository = new AggregateRepository(eventStoreConnection);

        var commandHandlerMap = new CommandHandlerMap(new Handlers(repository));

        return new Dispatcher(commandHandlerMap);
    }
}