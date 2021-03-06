﻿using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace DAO.Contexts
{
    public class DbContextIdentity : IdentityDbContext<User>
    {
        public DbContextIdentity(DbContextOptions<DbContextIdentity> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}
