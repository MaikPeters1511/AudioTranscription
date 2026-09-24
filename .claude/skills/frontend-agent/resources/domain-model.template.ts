export interface [EntityName] {
  id: string;
  // Domain Properties
}

// Optional: Value Objects oder statische Factory-Methoden für Domain-Logik
export class [EntityName]Model {
  static create(data: Partial<[EntityName]>): [EntityName] {
    return {
      id: data.id ?? crypto.randomUUID(),
      // Mappings
    };
  }
}
