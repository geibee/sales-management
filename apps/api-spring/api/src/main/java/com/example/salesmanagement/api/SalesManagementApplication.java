package com.example.salesmanagement.api;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.scheduling.annotation.EnableScheduling;

@SpringBootApplication(scanBasePackages = "com.example.salesmanagement")
@EnableScheduling
public class SalesManagementApplication {
    public static void main(String[] args) {
        SpringApplication.run(SalesManagementApplication.class, args);
    }
}
