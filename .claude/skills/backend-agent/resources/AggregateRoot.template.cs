using System;
using System.Collections.Generic;

namespace App.Domain.Common
{
    public abstract class Entity
    {
        public Guid Id { get; protected set; }
        
        protected Entity() { }
        
        protected Entity(Guid id)
        {
            Id = id;
        }
        
        // Boilerplate for equality operations often goes here
    }

    public abstract class AggregateRoot : Entity
    {
        private readonly List<object> _domainEvents = new();
        public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();

        protected AggregateRoot() { }
        
        protected AggregateRoot(Guid id) : base(id) { }

        protected void AddDomainEvent(object domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }
    }
}
