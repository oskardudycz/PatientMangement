using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
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
    
    private readonly CancellationToken ct = new CancellationTokenSource().Token;
    
    [Fact]
    public async Task EndToEndTest()
    {
        var eventStore = GetEventStore();
        var dispatcher = SetupDispatcher(eventStore);
        var connectionFactory = new ConnectionFactory("PatientManagement");

        var projections = new List<IProjection>
        {
            new WardViewProjection(connectionFactory),
            new PatientDemographicProjection(connectionFactory)
        };

        var projectionManager = new ProjectionManager.ProjectionManager(
            eventStore,
            connectionFactory,
            projections);

        await projectionManager.StartAsync(ct);
        
        var patientId = Guid.NewGuid();

        var admitPatient = new AdmitPatient(patientId, "Tony Ferguson", 32, DateTime.UtcNow, 10);
        await dispatcher.Dispatch(admitPatient, ct);

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
        await dispatcher.Dispatch(transferPatientOne, ct);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.NotNull(patient);
            Assert.Equal(transferPatientOne.WardNumber, patient.WardNumber);
        });

        var transferPatientTwo = new TransferPatient(patientId, 34);
        await dispatcher.Dispatch(transferPatientTwo, ct);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.NotNull(patient);
            Assert.Equal(transferPatientTwo.WardNumber, patient.WardNumber);
        });

        var dischargePatient = new DischargePatient(patientId);
        await dispatcher.Dispatch(dischargePatient, ct);
        
        retryPolicy.Execute(() =>
        {
            using var session = connectionFactory.Connect();
            var patient = session.Load<Patient>(patientId.ToString());
            
            Assert.Null(patient);
        });
    }

    Dispatcher SetupDispatcher(EventStoreClient eventStore)
    {
        var repository = new AggregateRepository(eventStore);

        var commandHandlerMap = new CommandHandlerMap(new Handlers(repository));

        return new Dispatcher(commandHandlerMap);
    }

    EventStoreClient GetEventStore()
    {
        const string connectionString = 
            "esdb://localhost:2113?tls=false";
        return new EventStoreClient(EventStoreClientSettings.Create(connectionString));
    }
}