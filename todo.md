# Todo


- import domain/capability description


- Implement openid support for login
-- support for groups in token to map to roles




- display domain and capability description to visualisation when on going over the domain/capability visualisation on mouseover

# Implement a login page that users use for login. 
-- Create a simple login page
-- I will provide image but use the svg as placeholder
-- The only text that should be on the page should be HERM-MAPPER
-- and the username, password and login button

# user management
-- Create a submenu called Users under the admin link
-- add the ability to create/update/delete users
-- a user should have the following data, given name, Last name, email, username. password
-- add the ability to add a user to a role
-- password reset
-- passwords should be minimal 12 characters, support password complexity, add a strength indicator.
-- passwords should be saved in a quantumproofed encryption key


# Password self service
-- Create a page that a user can see their information and can change password
-- Use this icon in the menu on the left of the admin page
<!-- User / profile icon -->
<svg xmlns="http://www.w3.org/2000/svg"
     viewBox="0 0 24 24"
     width="24"
     height="24"
     fill="none"
     stroke="currentColor"
     stroke-width="2"
     stroke-linecap="round"
     stroke-linejoin="round"
     aria-hidden="true">
  <path d="M20 21a8 8 0 0 0-16 0"/>
  <circle cx="12" cy="7" r="4"/>
</svg>
-- a user should click on the link to come to the passwordreset page
-- passwords should be minimal 12 characters, support password complexity, add a strength indicator.


# Roles
- Implement .net Role-based authorization 
- implement the following roles

-- Administrator
--- Has access to all features and can do anything in the system

-- Viewer role
--- Viewer is the standard role for new users
--- Read view for these pages, Dashboard, Product, Services, HERM TRM Catalogue

-- Contributor Role
--- Has access to Dashboard, Product,Services, HERM TRM Catalogue
--- Can Add/remove/modify products/services

# Api's
-- Admin can disable users
--- user and api keys should be disabled
-- Admin can disable api keys/remove keys from a user

- API endpoint
-- Swagger endpoint
-- Rate limit for api
-- User can create a personal api key
-- User can regenerate new key. Key will be shown once

- Auditing
-- All Create/Update/Delete operations should be logged in the common log
-- all operations should include who made the operations


# Version history
Implement version history

- Products
-- Add version history for all changes include who made the 

- Herm model
-- Add version history for all changes include who made the 

- Services
-- Add version history for all changes include who made them
-- Ability to restore the a service to previous version
