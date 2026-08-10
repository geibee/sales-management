package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.InventoryLot;

public record VersionedLot(InventoryLot value, int version) {}
